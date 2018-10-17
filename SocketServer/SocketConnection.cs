using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    public interface IConnection
    { }

    public class ConnectionAbortedException : OperationCanceledException
    {
        public ConnectionAbortedException() :
            this("The connection was aborted")
        {

        }

        public ConnectionAbortedException(string message) : base(message)
        {
        }

        public ConnectionAbortedException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    internal class SocketConnection : IConnection
    {
        private enum ParseEnum
        {
            Received = 1,
            Process_Head = 2,
            Process_Body = 3,
            Find_Body = 4
        }

        const int NOT_STARTED = 1;
        const int STARTED = 2;
        const int SHUTTING_DOWN = 3;
        const int SHUTDOWN = 4;
        volatile int _execState;

        Socket _socket;
        SocketListener _socketListener;
        ParseEnum _parseStatus;
        byte[] headBuffer = null;
        byte[] bodyBuffer = null;
        int maxMessageLength = 512;
        int headLength = 4;

        int offset = 0;
        int messageLength = 0;
        int prefixBytesDoneCount = 0;
        int prefixBytesDoneThisOp = 0;
        int messageBytesDoneCount = 0;
        int messageBytesDoneThisOp = 0;
        int remainingBytesToProcess = 0;

        public SocketConnection(Socket socket, SocketListener listener)
        {
            _execState = NOT_STARTED;
            _socket = socket;
            _socketListener = listener;
            _socketListener.OnSendMessage += SendMessage;
            _parseStatus = ParseEnum.Received;
            Interlocked.Increment(ref  _socketListener.ConnectedCount);
        }

        public void Start()
        {
            Task.Factory.StartNew(() =>
            {
                Interlocked.CompareExchange(ref _execState, STARTED, NOT_STARTED);
                var readEventArgs = _socketListener.SocketAsyncReceiveEventArgsPool.Pop();
                // 如果saea没有socket操作，意味着该对象没有绑定过事件handler
                // todo: 此种判定方式比较优雅，但是正确性没有经过检验
                if (readEventArgs.LastOperation == SocketAsyncOperation.None)
                {
                    readEventArgs.Completed += IO_Completed;
                }
                var willRaiseEvent = _socket.ReceiveAsync(readEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(readEventArgs);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            _socketListener.Scheduler);
        }

        public void Dispose()
        {
            Close();
        }

        public void Close()
        {
            Interlocked.CompareExchange(ref _execState, SHUTTING_DOWN, STARTED);
            // close the socket associated with the client
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch
            {
            }

            _socket.Close();
            Interlocked.CompareExchange(ref _execState, SHUTDOWN, SHUTTING_DOWN);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (_execState >= SHUTTING_DOWN)
            {
                return;
            }

            switch (_parseStatus)
            {
                case ParseEnum.Received:
                    {
                        prefixBytesDoneThisOp = 0;
                        messageBytesDoneThisOp = 0;
                        var read = e.BytesTransferred;
                        if (e.SocketError == SocketError.Success)
                        {
                            // 接收到FIN
                            if (read == 0)
                            {
                                InnerClose(e);
                                return;
                            }

                            remainingBytesToProcess = read;
                            _parseStatus = ParseEnum.Process_Head;
                        }
                        else
                        {
                            InnerClose(e);
                            throw new ConnectionAbortedException("e.SocketError != SocketError.Success");
                        }
                    }
                    break;
                case ParseEnum.Process_Head:
                    {
                        if (prefixBytesDoneCount < headLength)
                        {
                            if (prefixBytesDoneCount == 0)
                            {
                                headBuffer = ArrayPool<byte>.Shared.Rent(headLength);
                            }

                            if (remainingBytesToProcess >= headLength - prefixBytesDoneCount)
                            {
                                Buffer.BlockCopy(
                                    e.Buffer,
                                    0 + offset,
                                    headBuffer,
                                    prefixBytesDoneCount,
                                    headLength - prefixBytesDoneCount);

                                prefixBytesDoneThisOp = headLength - prefixBytesDoneCount;
                                prefixBytesDoneCount += prefixBytesDoneThisOp;
                                remainingBytesToProcess = remainingBytesToProcess - prefixBytesDoneThisOp;
                                messageLength = BitConverter.ToInt32(headBuffer, 0);
                                ArrayPool<byte>.Shared.Return(headBuffer, true);
                                if (messageLength > maxMessageLength)
                                {
                                    InnerClose(e);
                                    throw new ConnectionAbortedException("消息长度超过最大限制，直接丢弃");
                                }

                                _parseStatus = ParseEnum.Process_Body;
                            }
                            else
                            {
                                Buffer.BlockCopy(
                                    e.Buffer,
                                    0 + offset,
                                    headBuffer,
                                    prefixBytesDoneCount,
                                    remainingBytesToProcess);

                                prefixBytesDoneThisOp = remainingBytesToProcess;
                                prefixBytesDoneCount += prefixBytesDoneThisOp;
                                remainingBytesToProcess = 0;

                                offset = 0;

                                _parseStatus = ParseEnum.Received;
                                // 开始新一次recv
                                DoReceive(e);
                            }
                        }
                        else
                        {
                            _parseStatus = ParseEnum.Process_Body;
                        }
                    }
                    break;
                case ParseEnum.Process_Body:
                    {
                        if (messageBytesDoneCount == 0)
                        {
                            bodyBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                        }

                        if (remainingBytesToProcess >= messageLength - messageBytesDoneCount)
                        {
                            Buffer.BlockCopy(
                                e.Buffer,
                                prefixBytesDoneThisOp + offset,
                                bodyBuffer,
                                messageBytesDoneCount,
                                messageLength - messageBytesDoneCount);

                            messageBytesDoneThisOp = messageLength - messageBytesDoneCount;
                            messageBytesDoneCount += messageBytesDoneThisOp;
                            remainingBytesToProcess = remainingBytesToProcess - messageBytesDoneThisOp;

                            _parseStatus = ParseEnum.Find_Body;
                        }
                        else
                        {
                            Buffer.BlockCopy(
                                e.Buffer,
                                prefixBytesDoneThisOp + offset,
                                bodyBuffer,
                                messageBytesDoneCount,
                                remainingBytesToProcess);

                            messageBytesDoneThisOp = remainingBytesToProcess;
                            messageBytesDoneCount += messageBytesDoneThisOp;
                            remainingBytesToProcess = 0;

                            offset = 0;

                            _parseStatus = ParseEnum.Received;
                            // 开始新一次recv
                            DoReceive(e);
                        }
                    }
                    break;
                case ParseEnum.Find_Body:
                    {
                        ProcessMessage(bodyBuffer);
                        if (remainingBytesToProcess == 0)
                        {
                            _parseStatus = ParseEnum.Received;
                            // 开始新一次recv
                            DoReceive(e);
                        }
                        else
                        {
                            offset += (headLength + messageLength);

                            messageLength = 0;
                            prefixBytesDoneCount = 0;
                            prefixBytesDoneThisOp = 0;
                            messageBytesDoneCount = 0;
                            messageBytesDoneThisOp = 0;
                            _parseStatus = ParseEnum.Process_Head;
                        }
                    }
                    break;
            }
        }

        private void DoReceive(SocketAsyncEventArgs e)
        {
            Array.Clear(e.Buffer, 0, e.Buffer.Length);
            var willRaiseEvent = _socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        private void InnerClose(SocketAsyncEventArgs e)
        {
            _socketListener.SocketAsyncReceiveEventArgsPool.Push(e);
        }

        private void ProcessMessage(byte[] messageData)
        {
            _socketListener.SendingQueue.Add(messageData);
        }

        private void SendMessage(object sender, byte[] messageData)
        {
            Console.WriteLine(Encoding.UTF8.GetString(messageData));
            ArrayPool<byte>.Shared.Return(messageData);

            var sendEventArgs = _socketListener.SocketAsyncSendEventArgsPool.Pop();
            // 如果saea没有socket操作，意味着该对象没有绑定过事件handler
            // todo: 此种判定方式比较优雅，但是正确性没有经过检验
            if (sendEventArgs.LastOperation == SocketAsyncOperation.None)
            {
                sendEventArgs.Completed += IO_Completed;
            }
            
            var willRaiseEvent = _socket.SendAsync(sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(sendEventArgs);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            Array.Clear(e.Buffer, 0, e.Buffer.Length);
            _socketListener.SocketAsyncSendEventArgsPool.Push(e);
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            Task.Factory.StartNew(() =>
            {
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Receive:
                        ProcessReceive(e);
                        break;
                    case SocketAsyncOperation.Send:
                        ProcessSend(e);
                        break;
                    default:
                        throw new ArgumentException("未知的e.LastOperation");
                }
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            _socketListener.Scheduler);
        }
    }
}
