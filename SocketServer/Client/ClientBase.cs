using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer.Client
{
    public class ClientBase : IDisposable
    {
        private enum ParseEnum
        {
            Received = 1,
            Process_Head = 2,
            Process_Body = 3,
            Find_Body = 4
        }

        bool _debug;
        bool _disposed;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        Socket _client;
        IPEndPoint _remoteEndPoint;
        SocketAsyncEventArgs _readEventArgs;
        SocketAsyncEventArgs _sendEventArgs;

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

        public ClientBase(string address, int port, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _disposed = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _readEventArgs = new SocketAsyncEventArgs();
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        ~ClientBase()
        {
            //必须为false
            Dispose(false);
        }

        public virtual void Connect()
        {
            var connected = false;
            var connectEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = _remoteEndPoint
            };
            connectEventArgs.Completed += (sender, e) =>
            {
                connected = true;
            };

            if (_client.ConnectAsync(connectEventArgs))
            {
                while (!connected)
                {
                    if (!SpinWait.SpinUntil(() => connected, _connectTimeout))
                    {
                        throw new TimeoutException("Unable to connect within " + _connectTimeout + "ms");
                    }
                }
            }
            if (connectEventArgs.SocketError != SocketError.Success)
            {
                Close();
                throw new SocketException((int)connectEventArgs.SocketError);
            }
            if (!_client.Connected)
            {
                Close();
                throw new SocketException((int)SocketError.NotConnected);
            }

            // 至此，已经成功连接到远程服务端
        }

        public virtual void Close()
        {
            // close the socket associated with the client
            try
            {
                _client.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch
            {
            }
            _client.Close();
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            while (true)
            {
                #region ParseLogic
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
                                DoAbort("e.SocketError != SocketError.Success");
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
                                        DoAbort("消息长度超过最大限制，直接丢弃");
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
                            MessageReceived(bodyBuffer);
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
                #endregion
            }
        }

        private void DoReceive(SocketAsyncEventArgs e)
        {
            var willRaiseEvent = _client.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        private void DoClose()
        {
            Close();
        }

        private void DoAbort(string reason)
        {
            throw new ConnectionAbortedException(reason);
        }

        private void MessageReceived(byte[] messageData)
        {

        }

        public virtual void Send(byte[] messageData)
        {
            var willRaiseEvent = _client.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(_sendEventArgs);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            ArrayPool<byte>.Shared.Return((byte[])e.UserToken);
        }

        public virtual byte[] GetMessageBytes(string message)
        {
            var body = message;
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            var bytes = ArrayPool<byte>.Shared.Rent(head_bytes.Length + body_bytes.Length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);

            return bytes;
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
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
        }

        protected void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _client.Dispose();
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}

