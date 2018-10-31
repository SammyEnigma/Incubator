using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer
{
    public sealed class SocketListener : BaseListener, IConnectionEvents, IInnerCallBack
    {
        bool _debug;
        bool _disposed;
        Thread _sendMessageWorker;
        SemaphoreSlim _acceptedClientsSemaphore;
        BlockingCollection<Package> _sendingQueue;

        #region 事件
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler<Package> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion

        public SocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
            _disposed = false;
            _acceptedClientsSemaphore = new SemaphoreSlim(maxConnectionCount, maxConnectionCount);
            _sendingQueue = new BlockingCollection<Package>();
            _sendMessageWorker = new Thread(PorcessMessageQueue);
        }

        ~SocketListener()
        {
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

        protected override void InnerStop()
        {
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

        public void Send(SocketConnection connection, string message)
        {
            var length = 0;
            var bytes = GetMessageBytes(message, out length);
            _sendingQueue.Add(new Package { Connection = connection, MessageData = bytes, DataLength = length, RentFromPool = true });
        }

        public void Send(SocketConnection connection, byte[] messageData, int length, bool rentFromPool = true)
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

        private byte[] GetMessageBytes(string message, out int length)
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
                _sendingQueue.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}
