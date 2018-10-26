using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        protected bool _disposed;
        protected bool _debug;
        protected int _bufferSize;
        protected Socket _socket;
        protected Thread _sendMessageWorker;
        protected ManualResetEventSlim _shutdownEvent;
        protected BlockingCollection<Package> _sendingQueue;

        internal IOCompletionPortTaskScheduler Scheduler;
        internal ObjectPool<IPooledWapper> SocketAsyncReceiveEventArgsPool;
        internal ObjectPool<IPooledWapper> SocketAsyncSendEventArgsPool;

        ~BaseListener()
        {
            //必须为false
            Dispose(false);
        }

        public abstract void Start(IPEndPoint localEndPoint);
        public abstract void Stop();
        public abstract void Send(SocketConnection connection, string message);
        public abstract void Send(SocketConnection connection, byte[] messageData, int length, bool rentFromPool = true);
        protected virtual byte[] GetMessageBytes(string message, out int length)
        {
            var body = message;
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            length = head_bytes.Length + body_bytes.Length;
            var bytes = ArrayPool<byte>.Shared.Rent(length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);

            return bytes;
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
                _shutdownEvent.Dispose();
                _sendingQueue.Dispose();
                Scheduler.Dispose();
                SocketAsyncReceiveEventArgsPool.Dispose();
                SocketAsyncSendEventArgsPool.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }

    public class SocketListener : BaseListener, IConnectionEvents, IInnerCallBack, IDisposable
    {
        private int _maxConnectionCount;
        private volatile int _connectedCount;
        private SemaphoreSlim _acceptedClientsSemaphore;
        #region 事件
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler<Package> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion
        internal ConcurrentDictionary<int, SocketConnection> ConnectionList;

        public SocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _bufferSize = bufferSize;
            _maxConnectionCount = maxConnectionCount;
            _acceptedClientsSemaphore = new SemaphoreSlim(maxConnectionCount, maxConnectionCount);
            _sendingQueue = new BlockingCollection<Package>();
            _sendMessageWorker = new Thread(PorcessMessageQueue);
            _shutdownEvent = new ManualResetEventSlim(false);
            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, SocketConnection>();

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

        ~SocketListener()
        {
            //必须为false
            Dispose(false);
        }

        public override void Start(IPEndPoint localEndPoint)
        {
            _socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(localEndPoint);
            _socket.Listen(500);
            _sendMessageWorker.Start();
            StartAccept();
        }

        public override void Stop()
        {
            _shutdownEvent.Set();

            // 处理队列中剩余的消息
            Package package;
            while (_sendingQueue.TryTake(out package))
            {
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
                    OnInnerSending(package);
                    OnMessageSent?.Invoke(this, package);
                }
            }

            // 关闭所有连接
            SocketConnection conn;
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

        private void StartAccept(SocketAsyncEventArgs acceptEventArg = null)
        {
            if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
            {
                // 关闭事件触发，退出loop
                return;
            }

            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += Accept_Completed;
            }
            else
            {
                acceptEventArg.AcceptSocket = null;
            }

            _acceptedClientsSemaphore.Wait();
            var willRaiseEvent = _socket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        private void Accept_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
            {
                // 关闭事件触发，退出loop
                return;
            }

            SocketConnection connection = null;
            try
            {
                Interlocked.Increment(ref _connectedCount);
                connection = new SocketConnection(_connectedCount, e.AcceptSocket, this, _debug);
                connection.Start();
                ConnectionList.TryAdd(_connectedCount, connection);
                OnConnectionCreated?.Invoke(this, new ConnectionInfo { Num = connection.Id, Description = string.Empty, Time = DateTime.Now });
            }
            catch (SocketException ex)
            {
                Print(ex.Message);
            }
            catch (ConnectionAbortedException ex)
            {
                Print(ex.Message);
                connection.Close();
                _acceptedClientsSemaphore.Release();
                Interlocked.Decrement(ref _connectedCount);
                OnConnectionAborted?.Invoke(this, new ConnectionInfo { Num = connection.Id, Description = string.Empty, Time = DateTime.Now });
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }

            StartAccept(e);
        }

        public override void Send(SocketConnection connection, string message)
        {
            var length = 0;
            var bytes = GetMessageBytes(message, out length);
            _sendingQueue.Add(new Package { Connection = connection, MessageData = bytes, DataLength = length, RentFromPool = true });
        }

        public override void Send(SocketConnection connection, byte[] messageData, int length, bool rentFromPool = true)
        {
            _sendingQueue.Add(new Package { Connection = connection, MessageData = messageData, DataLength = length, RentFromPool = rentFromPool, NeedHead = true });
        }

        private void PorcessMessageQueue()
        {
            while (true)
            {
                if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
                {
                    // 关闭事件触发，退出loop
                    return;
                }

                var package = _sendingQueue.Take();
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
                    OnInnerSending(package);
                    OnMessageSent?.Invoke(this, package);
                }
            }
        }

        private void OnInnerSending(Package package)
        {
            package.Connection.InnerSend(package);
        }

        public void ConnectionClosed(ConnectionInfo connectionInfo)
        {
            OnConnectionClosed?.Invoke(this, connectionInfo);
        }

        public void MessageReceived(SocketConnection connection, byte[] messageData, int length)
        {
            OnMessageReceived?.Invoke(this, new Package { Connection = connection, MessageData = messageData, DataLength = length, RentFromPool = true });
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
                _acceptedClientsSemaphore.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}
