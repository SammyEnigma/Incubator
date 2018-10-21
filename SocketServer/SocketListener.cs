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
        public object Connection { set; get; }
        public byte[] MessageData { set; get; }
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
        void MessageReceived(SocketConnection connection, byte[] messageData);
        void ConnectionClosed(ConnectionInfo connectionInfo);
    }

    public interface IConnectionEvents
    {
        event EventHandler<ConnectionInfo> OnConnectionCreated;
        event EventHandler<ConnectionInfo> OnConnectionClosed;
        event EventHandler<ConnectionInfo> OnConnectionAborted;
        event EventHandler<byte[]> OnMessageReceived;
        event EventHandler<Package> OnMessageSending;
        event EventHandler<Package> OnMessageSent;
    }

    // todo: 也许叫reactor，或者eventpump更有利于今后开展抽象
    // 比如，客户端也叫listener就很不合适
    public abstract class BaseListener
    {
        protected bool Debug;
        protected int BufferSize;
        protected Socket Socket;
        protected Thread SendMessageWorker;
        protected ManualResetEventSlim ShutdownEvent;
        protected BlockingCollection<Package> SendingQueue;

        internal IOCompletionPortTaskScheduler Scheduler;
        internal ObjectPool<SocketAsyncEventArgs> SocketAsyncReceiveEventArgsPool;
        internal ObjectPool<SocketAsyncEventArgs> SocketAsyncSendEventArgsPool;

        public abstract void Start(IPEndPoint localEndPoint);
        public abstract void Stop();
        public abstract void Send(Package package);
        public abstract byte[] GetMessageBytes(string message);

        protected void Print(string message)
        {
            if (Debug)
            {
                Console.WriteLine(message);
            }
        }
    }

    public class SocketListener : BaseListener, IConnectionEvents, IInnerCallBack
    {
        private int _maxConnectionCount;
        private volatile int _connectedCount;
        private SemaphoreSlim _acceptedClientsSemaphore;
        #region 事件
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler<byte[]> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion
        internal ConcurrentDictionary<int, SocketConnection> ConnectionList;

        public SocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
        {
            Debug = debug;
            BufferSize = bufferSize;
            _maxConnectionCount = maxConnectionCount;
            _acceptedClientsSemaphore = new SemaphoreSlim(maxConnectionCount, maxConnectionCount);
            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, SocketConnection>();
            SendingQueue = new BlockingCollection<Package>();
            SendMessageWorker = new Thread(PorcessMessageQueue);
            ShutdownEvent = new ManualResetEventSlim(false);

            SocketAsyncEventArgs socketAsyncEventArgs = null;
            SocketAsyncSendEventArgsPool = new ObjectPool<SocketAsyncEventArgs>(maxConnectionCount, null);
            SocketAsyncReceiveEventArgsPool = new ObjectPool<SocketAsyncEventArgs>(maxConnectionCount, null);
            for (int i = 0; i < maxConnectionCount; i++)
            {
                socketAsyncEventArgs = new SocketAsyncEventArgs();
                socketAsyncEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(bufferSize), 0, bufferSize);
                SocketAsyncReceiveEventArgsPool.Push(socketAsyncEventArgs);

                socketAsyncEventArgs = new SocketAsyncEventArgs();
                socketAsyncEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(bufferSize), 0, bufferSize);
                SocketAsyncSendEventArgsPool.Push(socketAsyncEventArgs);
            }
        }

        public override void Start(IPEndPoint localEndPoint)
        {
            Socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            Socket.Bind(localEndPoint);
            Socket.Listen(500);
            SendMessageWorker.Start();
            StartAccept();
        }

        public override void Stop()
        {
            ShutdownEvent.Set();

            // 处理队列中剩余的消息
            Package package;
            while (SendingQueue.TryTake(out package))
            {
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
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
            Socket.Close();
            Dispose();
        }

        private void StartAccept(SocketAsyncEventArgs acceptEventArg = null)
        {
            if (ShutdownEvent.Wait(0)) // 仅检查标志，立即返回
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
            var willRaiseEvent = Socket.AcceptAsync(acceptEventArg);
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
            if (ShutdownEvent.Wait(0)) // 仅检查标志，立即返回
            {
                // 关闭事件触发，退出loop
                return;
            }

            SocketConnection connection = null;
            try
            {
                Interlocked.Increment(ref _connectedCount);
                connection = new SocketConnection(_connectedCount, e.AcceptSocket, this, Debug);
                ConnectionList.TryAdd(_connectedCount, connection);
                Interlocked.Increment(ref _connectedCount);
                connection.Start();
                OnConnectionCreated?.Invoke(this, new ConnectionInfo { Num = connection.Id, Description = string.Empty, Time = DateTime.Now });
            }
            catch (SocketException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (ConnectionAbortedException ex)
            {
                Console.WriteLine(ex.Message);
                connection.Close();
                _acceptedClientsSemaphore.Release();
                Interlocked.Decrement(ref _connectedCount);
                OnConnectionAborted?.Invoke(this, new ConnectionInfo { Num = connection.Id, Description = string.Empty, Time = DateTime.Now });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            StartAccept(e);
        }

        public override void Send(Package package)
        {
            this.SendingQueue.Add(package);
        }

        public override byte[] GetMessageBytes(string message)
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

        private void PorcessMessageQueue()
        {
            while (true)
            {
                if (ShutdownEvent.Wait(0)) // 仅检查标志，立即返回
                {
                    // 关闭事件触发，退出loop
                    return;
                }

                var package = SendingQueue.Take();
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
            ((SocketConnection)package.Connection).InnerSend(package);
        }

        public void ConnectionClosed(ConnectionInfo connectionInfo)
        {
            OnConnectionClosed?.Invoke(this, connectionInfo);
        }

        public void MessageReceived(SocketConnection connection, byte[] messageData)
        {
            OnMessageReceived?.Invoke(connection, messageData);
        }

        private void Dispose()
        {
            Scheduler.Dispose();
            _acceptedClientsSemaphore.Dispose();
            SocketAsyncSendEventArgsPool.Dispose();
            SocketAsyncReceiveEventArgsPool.Dispose();
        }
    }
}
