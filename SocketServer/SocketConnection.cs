using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    public sealed class SocketConnection : BaseConnection
    {
        class FixHeaderDecoder
        {
            private enum ParseEnum
            {
                Received = 1,
                Process_Head = 2,
                Process_Body = 3,
                Find_Body = 4
            }

            bool _debug;
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
            SocketConnection _connection;

            public FixHeaderDecoder(SocketConnection connection, bool debug = false)
            {
                _debug = debug;
                _parseStatus = ParseEnum.Received;
                _connection = connection;
            }

            public void ProcessReceive(SocketAsyncEventArgs e)
            {
                Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
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
                                        _connection.DoClose();
                                        return;
                                    }

                                    remainingBytesToProcess = read;
                                    _parseStatus = ParseEnum.Process_Head;
                                }
                                else
                                {
                                    _connection.DoAbort("e.SocketError != SocketError.Success");
                                    return;
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
                                            _connection.DoAbort("消息长度为0或超过最大限制，直接丢弃");
                                            return;
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
                                        _connection.DoReceive(e);
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
                                    _connection.DoReceive(e);
                                    return;
                                }
                            }
                            break;
                        case ParseEnum.Find_Body:
                            {
                                _connection.MessageReceived(bodyBuffer, messageLength);
                                if (remainingBytesToProcess == 0)
                                {
                                    messageLength = 0;
                                    prefixBytesDoneCount = 0;
                                    messageBytesDoneCount = 0;
                                    _parseStatus = ParseEnum.Received;
                                    // 开始新一次recv
                                    _connection.DoReceive(e);
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

            private void Print(string message)
            {
                if (_debug)
                {
                    Console.WriteLine(message);
                }
            }
        }

        bool _disposed;
        FixHeaderDecoder _decoder;

        public SocketConnection(int id, Socket socket, BaseListener listener, bool debug = false) 
            : base(id, socket, listener, debug)
        {
            _disposed = false;
            _decoder = new FixHeaderDecoder(this, debug);

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

        protected override void InnerStart()
        {
            Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
            Interlocked.CompareExchange(ref _execStatus, STARTED, NOT_STARTED);
            var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
            if (!willRaiseEvent)
            {
                ProcessReceive(_readEventArgs);
            }
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (_execStatus == STARTED)
                _decoder.ProcessReceive(e);
        }

        private void DoReceive(SocketAsyncEventArgs e)
        {
            var willRaiseEvent = _socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        internal void MessageReceived(byte[] messageData, int length)
        {
            (_socketListener as IInnerCallBack).MessageReceived(this, messageData, length);
        }

        internal void InnerSend(Package package)
        {
            if (package.RentFromPool)
                _sendEventArgs.UserToken = package.MessageData; // 预先保存下来，使用完毕需要回收到ArrayPool中

            // todo: 缓冲区一次发送不完的情况处理
            if (package.NeedHead)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(package.DataLength), 0, _sendEventArgs.Buffer, 0, 4);
                Buffer.BlockCopy(package.MessageData, 0, _sendEventArgs.Buffer, 4, package.DataLength);
                // todo: abort和这里的send会有一个race condition，目前考虑的解决办法是abort那里自旋一段时间等
                // 当次发送完毕了再予以关闭
                _sendEventArgs.SetBuffer(0, package.DataLength + 4);
            }
            else
            {
                Buffer.BlockCopy(package.MessageData, 0, _sendEventArgs.Buffer, 0, package.DataLength);
                // todo: abort和这里的send会有一个race condition，目前考虑的解决办法是abort那里自旋一段时间等
                // 当次发送完毕了再予以关闭
                _sendEventArgs.SetBuffer(0, package.DataLength);
            }

            var willRaiseEvent = _socket.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(_sendEventArgs);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.UserToken != null)
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

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _sendEventArgs.UserToken = null;
                _sendEventArgs.Completed -= IO_Completed;
                _readEventArgs.UserToken = null;
                _readEventArgs.Completed -= IO_Completed;
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;

            base.Dispose();
        }
    }
}
