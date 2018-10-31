using System;
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

    public abstract class BaseConnection : IConnection, IDisposable
    {
        int _id;
        bool _debug;
        bool _disposed;

        protected const int NOT_STARTED = 1;
        protected const int STARTED = 2;
        protected const int SHUTTING_DOWN = 3;
        protected const int SHUTDOWN = 4;
        protected volatile int _execStatus;

        protected Socket _socket;
        protected BaseListener _socketListener;
        protected SocketAsyncEventArgs _readEventArgs;
        protected SocketAsyncEventArgs _sendEventArgs;
        protected PooledSocketAsyncEventArgs _pooledReadEventArgs;
        protected PooledSocketAsyncEventArgs _pooledSendEventArgs;

        internal int Id { get { return _id; } }

        public BaseConnection(int id, Socket socket, BaseListener listener, bool debug = false)
        {
            _id = id;
            _debug = debug;
            _disposed = false;
            _execStatus = NOT_STARTED;
            _socketListener = listener;
            _socket = socket;
        }

        ~BaseConnection()
        {
            //必须为false
            Dispose(false);
        }

        public async void Start()
        {
            await Task.Factory.StartNew(() =>
            {
                InnerStart();
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            _socketListener.Scheduler);
        }

        protected abstract void InnerStart();

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

        internal void DoClose()
        {
            Close();
            (_socketListener as IInnerCallBack).ConnectionClosed(new ConnectionInfo { Num = this.Id, Description = string.Empty, Time = DateTime.Now });
        }

        internal void DoAbort(string reason)
        {
            Close();
            Dispose();
            // todo: 直接被设置到task的result里面了，在listener的线程中抓不到这个异常
            // 类似的其它异常也需要注意这种情况
            throw new ConnectionAbortedException(reason);
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
                _socket.Dispose();
                _socketListener.SocketAsyncSendEventArgsPool.Put(_pooledSendEventArgs);
                _socketListener.SocketAsyncReceiveEventArgsPool.Put(_pooledReadEventArgs);
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
