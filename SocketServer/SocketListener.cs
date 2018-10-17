using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Incubator.SocketServer
{
    public class SocketListener
    {
        private Socket _socket;
        private int _bufferSize;
        private int _maxConnectionCount;
        private ManualResetEventSlim _shutdownEvent;
        internal volatile int ConnectedCount;
        internal Thread SendMessageWorker;
        internal SemaphoreSlim AcceptedClientsSemaphore;
        internal BlockingCollection<byte[]> SendingQueue;
        internal IOCompletionPortTaskScheduler Scheduler;
        internal SocketAsyncEventArgsPool SocketAsyncReceiveEventArgsPool;
        internal SocketAsyncEventArgsPool SocketAsyncSendEventArgsPool;
        #region 事件
        public event EventHandler OnServerStarting;
        public event EventHandler OnServerStarted;
        public event EventHandler<string> OnConnectionCreated;
        public event EventHandler<string> OnServerStopping;
        public event EventHandler<string> OnServerStopped;
        public event EventHandler<byte[]> OnSendMessage;
        #endregion
        internal ConcurrentDictionary<int, SocketConnection> ConnectionList;

        public SocketListener(int maxConnectionCount, int bufferSize)
        {
            _bufferSize = bufferSize;
            _maxConnectionCount = 0;
            _maxConnectionCount = maxConnectionCount;
            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, SocketConnection>();
            SendingQueue = new BlockingCollection<byte[]>();
            SendMessageWorker = new Thread(PorcessMessageQueue);
            _shutdownEvent = new ManualResetEventSlim(true);
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
            _socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(localEndPoint);
            _socket.Listen(500);
            SendMessageWorker.Start();
            StartAccept();
        }

        public void Stop()
        {
            _shutdownEvent.Set();

            // 处理队列中剩余的消息
            byte[] messageData;
            while (SendingQueue.TryTake(out messageData))
            {
                if (messageData != null)
                {
                    OnSendMessage?.Invoke(this, messageData);
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
                connection = new SocketConnection(e.AcceptSocket, this);
                ConnectionList.TryAdd(ConnectedCount, connection);
                connection.Start();
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

                var messageData = SendingQueue.Take();
                if (messageData != null)
                {
                    OnSendMessage?.Invoke(this, messageData);
                }
            }
        }

        private void Dispose()
        {
            Scheduler.Dispose();
            AcceptedClientsSemaphore.Dispose();
            SocketAsyncSendEventArgsPool.Dispose();
            SocketAsyncReceiveEventArgsPool.Dispose();
        }
    }
}
