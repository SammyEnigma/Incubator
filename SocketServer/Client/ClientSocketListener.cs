using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer.Client
{
    /*
    public class ClientSocketListener : BaseListener
    {
        private bool _debug;
        private Socket _socket;
        private int _bufferSize;
        private int _maxConnectionCount;
        private ManualResetEventSlim _shutdownEvent;
        internal volatile int ConnectedCount;
        internal Thread SendMessageWorker;
        internal SemaphoreSlim AcceptedClientsSemaphore;
        internal BlockingCollection<Package> SendingQueue;
        internal IOCompletionPortTaskScheduler Scheduler;
        internal SocketAsyncEventArgsPool SocketAsyncReceiveEventArgsPool;
        internal SocketAsyncEventArgsPool SocketAsyncSendEventArgsPool;
        #region 事件
        public event EventHandler OnServerStarting;
        public event EventHandler OnServerStarted;
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler OnServerStopping;
        public event EventHandler OnServerStopped;
        public event EventHandler<byte[]> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        internal event EventHandler<Package> Sending;
        public event EventHandler<Package> OnMessageSent;
        #endregion
        internal ConcurrentDictionary<int, SocketConnection> ConnectionList;

        public ClientSocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _bufferSize = bufferSize;
            _maxConnectionCount = 0;
            _maxConnectionCount = maxConnectionCount;
            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, SocketConnection>();
            SendingQueue = new BlockingCollection<Package>();
            SendMessageWorker = new Thread(PorcessMessageQueue);
            _shutdownEvent = new ManualResetEventSlim(false);
            AcceptedClientsSemaphore = new SemaphoreSlim(maxConnectionCount, maxConnectionCount);

            SocketAsyncEventArgs socketAsyncEventArgs = null;
            SocketAsyncSendEventArgsPool = new SocketAsyncEventArgsPool(maxConnectionCount);
            SocketAsyncReceiveEventArgsPool = new SocketAsyncEventArgsPool(maxConnectionCount);
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

        public void Start(IPEndPoint localEndPoint)
        {
            OnServerStarting?.Invoke(this, EventArgs.Empty);
            _socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(localEndPoint);
            _socket.Listen(500);
            SendMessageWorker.Start();
            OnServerStarted?.Invoke(this, EventArgs.Empty);
            StartAccept();
        }

        public void Stop()
        {
            _shutdownEvent.Set();
            OnServerStopping?.Invoke(this, EventArgs.Empty);

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
            _socket.Close();
            Dispose();
            OnServerStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Send(Package package)
        {
            this.SendingQueue.Add(package);
        }

        public byte[] GetMessageBytes(string message)
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

            AcceptedClientsSemaphore.Wait();
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
                Interlocked.Increment(ref ConnectedCount);
                connection = new SocketConnection(ConnectedCount, e.AcceptSocket, this, _debug);
                connection.OnMessageReceived += MessageReceived;
                connection.OnConnectionClosed += ConnectionClosed;
                ConnectionList.TryAdd(ConnectedCount, connection);

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
                AcceptedClientsSemaphore.Release();
                Interlocked.Decrement(ref ConnectedCount);
                OnConnectionAborted?.Invoke(this, new ConnectionInfo { Num = connection.Id, Description = string.Empty, Time = DateTime.Now });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            StartAccept(e);
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

                var package = SendingQueue.Take();
                if (package != null)
                {
                    OnMessageSending?.Invoke(this, package);
                    Sending?.Invoke(this, package);
                    OnMessageSent?.Invoke(this, package);
                }
            }
        }

        private void ConnectionClosed(object sender, ConnectionInfo e)
        {
            OnConnectionClosed?.Invoke(sender, e);
        }

        private void MessageReceived(object sender, byte[] e)
        {
            OnMessageReceived?.Invoke(sender, e);
        }

        private void Dispose()
        {
            Scheduler.Dispose();
            AcceptedClientsSemaphore.Dispose();
            SocketAsyncSendEventArgsPool.Dispose();
            SocketAsyncReceiveEventArgsPool.Dispose();
        }

        private void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }
    }
    */
}
