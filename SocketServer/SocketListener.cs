using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Incubator.SocketServer
{
    internal class SocketListener
    {
        private object _lockobj;
        private Socket _socket;
        private int _bufferSize;
        private int _maxConnectionCount;
        internal volatile int ConnectedCount;
        internal Thread SendMessageWorker;
        internal SemaphoreSlim AcceptedClientsSemaphore;
        internal BlockingCollection<byte[]> SendingQueue;
        internal IOCompletionPortTaskScheduler Scheduler;
        internal SocketAsyncEventArgsPool SocketAsyncReceiveEventArgsPool;
        internal SocketAsyncEventArgsPool SocketAsyncSendEventArgsPool;
        #region 事件
        internal event EventHandler OnServerStarting;
        internal event EventHandler OnServerStarted;
        internal event EventHandler<string> OnConnectionCreated;
        internal event EventHandler<string> OnServerStopping;
        internal event EventHandler<string> OnServerStopped;
        internal event EventHandler<byte[]> OnSendMessage;
        #endregion
        internal ConcurrentDictionary<int, SocketConnection> ConnectionList;

        public SocketListener(int maxConnectionCount, int bufferSize)
        {
            _lockobj = new object();
            _bufferSize = bufferSize;
            _maxConnectionCount = 0;
            _maxConnectionCount = maxConnectionCount;
            Scheduler = new IOCompletionPortTaskScheduler(12, 12);
            ConnectionList = new ConcurrentDictionary<int, SocketConnection>();
            SendingQueue = new BlockingCollection<byte[]>();
            SendMessageWorker = new Thread(PorcessMessageQueue);
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
            SocketConnection conn;
            foreach (var key in ConnectionList.Keys)
            {
                if (ConnectionList.TryRemove(key, out conn))
                {
                    conn.Stop();
                }
            }
            _socket.Close();
            Dispose();
        }

        private void StartAccept(SocketAsyncEventArgs acceptEventArg = null)
        {
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
            try
            {
                Interlocked.Increment(ref ConnectedCount);
                SocketConnection connection = new SocketConnection(e.AcceptSocket, this);
                ConnectionList.TryAdd(ConnectedCount, connection);
                connection.Start();
            }
            catch (SocketException ex)
            {
                Console.WriteLine(ex.Message);
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
