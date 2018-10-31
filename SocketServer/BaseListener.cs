using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Incubator.SocketServer
{
    public class Package
    {
        public SocketConnection Connection { set; get; }
        public byte[] MessageData { set; get; }
        public int DataLength { set; get; }
        public bool RentFromPool { set; get; }
        public bool NeedHead { set; get; }
    }

    public class ConnectionInfo
    {
        public int Num { set; get; }
        public string Description { set; get; }
        public DateTime Time { set; get; }

        public override string ToString()
        {
            return string.Format("Id：{0}，描述：[{1}]，时间：{2}", Num, Description == string.Empty ? "空" : Description, Time);
        }
    }

    // 用来模拟友元（访问控制）
    public interface IInnerCallBack
    {
        void MessageReceived(SocketConnection connection, byte[] messageData, int length);
        void ConnectionClosed(ConnectionInfo connectionInfo);
    }

    public interface IConnectionEvents
    {
        event EventHandler<ConnectionInfo> OnConnectionCreated;
        event EventHandler<ConnectionInfo> OnConnectionClosed;
        event EventHandler<ConnectionInfo> OnConnectionAborted;
        event EventHandler<Package> OnMessageReceived;
        event EventHandler<Package> OnMessageSending;
        event EventHandler<Package> OnMessageSent;
    }

    // todo: 也许叫reactor，或者eventpump更有利于今后开展抽象
    // 比如，客户端也叫listener就很不合适
    public abstract class BaseListener : IDisposable
    {
        bool _debug;
        bool _disposed;
        int _bufferSize;
        int _maxConnectionCount;

        protected volatile int _connectedCount;
        protected Socket _socket;
        protected ManualResetEventSlim _shutdownEvent;

        internal IOCompletionPortTaskScheduler Scheduler;
        internal ConcurrentDictionary<int, BaseConnection> ConnectionList;
        internal ObjectPool<IPooledWapper> SocketAsyncReceiveEventArgsPool;
        internal ObjectPool<IPooledWapper> SocketAsyncSendEventArgsPool;

        public BaseListener(int maxConnectionCount, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _disposed = false;
            _bufferSize = bufferSize;
            _maxConnectionCount = maxConnectionCount;
            _shutdownEvent = new ManualResetEventSlim(false);

            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, BaseConnection>();

            SocketAsyncSendEventArgsPool = new ObjectPool<IPooledWapper>(maxConnectionCount, 12, (pool) =>
            {
                var socketAsyncEventArgs = new SocketAsyncEventArgs();
                socketAsyncEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(bufferSize), 0, bufferSize);
                return new PooledSocketAsyncEventArgs(pool, socketAsyncEventArgs);
            });

            SocketAsyncReceiveEventArgsPool = new ObjectPool<IPooledWapper>(maxConnectionCount, 12, (pool) =>
            {
                var socketAsyncEventArgs = new SocketAsyncEventArgs();
                socketAsyncEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(bufferSize), 0, bufferSize);
                return new PooledSocketAsyncEventArgs(pool, socketAsyncEventArgs);
            });
        }

        ~BaseListener()
        {
            Dispose(false);
        }

        public abstract void Start(IPEndPoint localEndPoint);

        public void Stop()
        {
            _shutdownEvent.Set();
            InnerStop();
            // 关闭所有连接
            BaseConnection conn;
            foreach (var key in ConnectionList.Keys)
            {
                if (ConnectionList.TryRemove(key, out conn))
                {
                    conn.Dispose();
                }
            }
            _socket.Close();
            Dispose();
        }

        protected virtual void InnerStop()
        {
            // do nothing here
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
                _shutdownEvent.Dispose();
                Scheduler.Dispose();
                SocketAsyncReceiveEventArgsPool.Dispose();
                SocketAsyncSendEventArgsPool.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
