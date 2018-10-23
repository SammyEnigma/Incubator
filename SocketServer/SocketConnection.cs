using System;
using System.Buffers;
using System.Net.Sockets;
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

    public class SocketConnection : IConnection, IDisposable
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
        bool _disposed;
        Socket _socket;
        BaseListener _socketListener;
        PooledSocketAsyncEventArgs _pooledReadEventArgs;
        PooledSocketAsyncEventArgs _pooledSendEventArgs;
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

        internal int Id { get { return _id; } }

        public SocketConnection(int id, Socket socket, BaseListener listener, bool debug)
        {
            _id = id;
            _debug = debug;
            _execStatus = NOT_STARTED;
            _socketListener = listener;
            _parseStatus = ParseEnum.Received;
            _socket = socket;
            _pooledReadEventArgs = _socketListener.SocketAsyncReceiveEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _readEventArgs = _pooledReadEventArgs.SocketAsyncEvent;
            _readEventArgs.Completed += IO_Completed;

            _pooledSendEventArgs = _socketListener.SocketAsyncSendEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _sendEventArgs = _pooledSendEventArgs.SocketAsyncEvent;
            _sendEventArgs.Completed += IO_Completed;
        }

        ~SocketConnection()
        {
            //必须为false
            Dispose(false);
        }

        public void Start()
        {
            Task.Factory.StartNew(() =>
            {
                Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
                Interlocked.CompareExchange(ref _execStatus, STARTED, NOT_STARTED);
                var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(_readEventArgs);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            _socketListener.Scheduler);
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
                                    if (messageLength == 0 || messageLength > maxMessageLength)
                                    {
                                        DoAbort("消息长度为0或超过最大限制，直接丢弃");
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
            var willRaiseEvent = _socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        private void DoClose()
        {
            Close();
            (_socketListener as IInnerCallBack).ConnectionClosed(new ConnectionInfo { Num = this.Id, Description = string.Empty, Time = DateTime.Now });
        }

        private void DoAbort(string reason)
        {
            Close();
            Dispose();
            // todo: 直接被设置到task的result里面了，在listener的线程中抓不到这个异常
            // 类似的其它异常也需要注意这种情况
            throw new ConnectionAbortedException(reason);
        }

        private void MessageReceived(byte[] messageData)
        {
            (_socketListener as IInnerCallBack).MessageReceived(this, messageData);
        }

        internal void InnerSend(Package package)
        {
            _sendEventArgs.UserToken = package.MessageData; // 预先保存下来，使用完毕需要回收到ArrayPool中
            // todo: 缓冲区一次发送不完的情况处理
            Buffer.BlockCopy(package.MessageData, 0,  _sendEventArgs.Buffer, 0, package.DataLength);
            // todo: abort和这里的send会有一个race condition，目前考虑的解决办法是abort那里自旋一段时间等
            // 当次发送完毕了再予以关闭
            _sendEventArgs.SetBuffer(0, package.DataLength);
            var willRaiseEvent = _socket.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(_sendEventArgs);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            ArrayPool<byte>.Shared.Return((byte[])e.UserToken);
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

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _socket.Dispose();
                _sendEventArgs.UserToken = null;
                _sendEventArgs.Completed -= IO_Completed;
                _readEventArgs.UserToken = null;
                _readEventArgs.Completed -= IO_Completed;
                _socketListener.SocketAsyncSendEventArgsPool.Put(_pooledSendEventArgs);
                _socketListener.SocketAsyncReceiveEventArgsPool.Put(_pooledReadEventArgs);
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
