using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    public class StreamedConnection : IConnection, IDisposable
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
        byte[] _readbuffer;
        byte[] _sendbuffer;
        BaseListener _socketListener;
        PooledSocketAsyncEventArgs _pooledReadEventArgs;
        PooledSocketAsyncEventArgs _pooledSendEventArgs;
        SocketAsyncEventArgs _readEventArgs;
        SocketAsyncEventArgs _sendEventArgs;
        SocketAwaitable _readAwait;
        SocketAwaitable _sendAwait;

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

        public StreamedConnection(int id, Socket socket, BaseListener listener, bool debug)
        {
            _id = id;
            _debug = debug;
            _execStatus = NOT_STARTED;
            _socketListener = listener;
            _parseStatus = ParseEnum.Received;
            _socket = socket;
            _pooledReadEventArgs = _socketListener.SocketAsyncReceiveEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _readEventArgs = _pooledReadEventArgs.SocketAsyncEvent;

            _pooledSendEventArgs = _socketListener.SocketAsyncSendEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _sendEventArgs = _pooledSendEventArgs.SocketAsyncEvent;

            _readbuffer = _readEventArgs.Buffer;
            _sendbuffer = _sendEventArgs.Buffer;
            _readAwait = new SocketAwaitable(_readEventArgs);
            _sendAwait = new SocketAwaitable(_sendEventArgs);
        }

        ~StreamedConnection()
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

        private void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        private async Task FillBuffer(int count)
        {
            var read = 0;
            do
            {
                _readEventArgs.SetBuffer(read, count - read);
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    break;
                }
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
        }

        public async Task<int> ReadInt32()
        {
            await FillBuffer(4);
            return (int)(_readbuffer[0] | _readbuffer[1] << 8 | _readbuffer[2] << 16 | _readbuffer[3] << 24);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
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
                _readEventArgs.UserToken = null;
                _socketListener.SocketAsyncSendEventArgsPool.Put(_pooledSendEventArgs);
                _socketListener.SocketAsyncReceiveEventArgsPool.Put(_pooledReadEventArgs);
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
