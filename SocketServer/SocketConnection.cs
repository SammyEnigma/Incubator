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
        public ConnectionAbortedException() 
            : this("The connection was aborted")
        {

        }

        public ConnectionAbortedException(string message) 
            : base(message)
        {
        }

        public ConnectionAbortedException(string message, Exception inner) 
            : base(message, inner)
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
        volatile int _execStatus;

        int _id;
        bool _debug;
        Socket _socket;
        SocketListener _socketListener;
        SocketAsyncEventArgs readEventArgs;
        SocketAsyncEventArgs sendEventArgs;
        internal event EventHandler<ConnectionInfo> OnConnectionClosed;

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

        internal int Id { get { return _id; } }

        public SocketConnection(int id, Socket socket, SocketListener listener, bool debug)
        {
            _id = id;
            _debug = debug;
            _execStatus = NOT_STARTED;
            _socket = socket;
            _socketListener = listener;
            _socketListener.Sending += SendMessage;
            _parseStatus = ParseEnum.Received;
            readEventArgs = _socketListener.SocketAsyncReceiveEventArgsPool.Pop();
            readEventArgs.Completed += IO_Completed;
            sendEventArgs = _socketListener.SocketAsyncSendEventArgsPool.Pop();
            sendEventArgs.Completed += IO_Completed;
            Interlocked.Increment(ref _socketListener.ConnectedCount);
        }

        public void Start()
        {
            Task.Factory.StartNew(() =>
            {
                Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
                Interlocked.CompareExchange(ref _execStatus, STARTED, NOT_STARTED);
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
            Interlocked.CompareExchange(ref _execStatus, SHUTTING_DOWN, STARTED);
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
            _socketListener.SocketAsyncSendEventArgsPool.Push(sendEventArgs);
            _socketListener.SocketAsyncReceiveEventArgsPool.Push(readEventArgs);
            Interlocked.CompareExchange(ref _execStatus, SHUTDOWN, SHUTTING_DOWN);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
            if (_execStatus >= SHUTTING_DOWN)
            {
                return;
            }

            while (true)
            {
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
                                    DoClose();
                                    return;
                                }

                                remainingBytesToProcess = read;
                                _parseStatus = ParseEnum.Process_Head;
                            }
                            else
                            {
                                DoAbort();
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
                                        DoAbort();
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
                                    return;
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
                                return;
                            }
                        }
                        break;
                    case ParseEnum.Find_Body:
                        {
                            ProcessMessage(bodyBuffer);
                            if (remainingBytesToProcess == 0)
                            {
                                messageLength = 0;
                                prefixBytesDoneCount = 0;
                                messageBytesDoneCount = 0;
                                _parseStatus = ParseEnum.Received;
                                // 开始新一次recv
                                DoReceive(e);
                                return;
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
        }

        private void DoReceive(SocketAsyncEventArgs e)
        {
            var willRaiseEvent = _socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        private void DoClose()
        {
            Close();
            OnConnectionClosed?.Invoke(this, new ConnectionInfo { Num = this.Id, Description = string.Empty, Time = DateTime.Now });
        }

        private void DoAbort()
        {
            _socketListener.SocketAsyncSendEventArgsPool.Push(sendEventArgs);
            _socketListener.SocketAsyncReceiveEventArgsPool.Push(readEventArgs);
        }

        private void ProcessMessage(byte[] messageData)
        {
            var package = new Package { connection = this, MessageData = messageData };
            _socketListener.SendingQueue.Add(package);
        }

        private static void SendMessage(object sender, Package package)
        {
            ArrayPool<byte>.Shared.Return(package.MessageData);

            var socket = package.connection._socket;
            var sendArgs = package.connection.sendEventArgs;
            var willRaiseEvent = socket.SendAsync(sendArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(sendArgs);
            }
        }

        private static void ProcessSend(SocketAsyncEventArgs e)
        {
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            Task.Factory.StartNew(() =>
            {
                Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
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

        private void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }
    }
}
