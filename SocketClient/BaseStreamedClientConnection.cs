using Incubator.SocketServer;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketClient
{
    public class BaseStreamedClientConnection : IDisposable
    {
        bool _debug;
        bool _disposed;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        Socket _socket;
        IPEndPoint _remoteEndPoint;
        SocketAsyncEventArgs _readEventArgs;
        SocketAsyncEventArgs _sendEventArgs;

        public BaseStreamedClientConnection(string address, int port, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _disposed = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            _readEventArgs = new SocketAsyncEventArgs();            
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);

            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);

            _readAwait = new SocketAwaitable(_readEventArgs, listener, debug);
            _sendAwait = new SocketAwaitable(_sendEventArgs, listener, debug);
        }

        ~BaseStreamedClientConnection()
        {
            Dispose(false);
        }

        public virtual void Connect()
        {
            var connected = false;
            var connectEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = _remoteEndPoint
            };
            connectEventArgs.Completed += (sender, e) =>
            {
                connected = true;
            };

            if (_socket.ConnectAsync(connectEventArgs))
            {
                while (!connected)
                {
                    if (!SpinWait.SpinUntil(() => connected, _connectTimeout))
                    {
                        throw new TimeoutException("Unable to connect within " + _connectTimeout + "ms");
                    }
                }
            }
            if (connectEventArgs.SocketError != SocketError.Success)
            {
                Close();
                throw new SocketException((int)connectEventArgs.SocketError);
            }
            if (!_socket.Connected)
            {
                Close();
                throw new SocketException((int)SocketError.NotConnected);
            }

            // 至此，已经成功连接到远程服务端
            Task.Run(() => 
            {
                var willRaiseEvent = _client.ReceiveAsync(_readEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(_readEventArgs);
                }
            });
        }

        public virtual void Close()
        {
            // close the socket associated with the client
            try
            {
                _client.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch
            {
            }
            _client.Close();
        }

        private void DoClose()
        {
            Close();
        }

        private void DoAbort(string reason)
        {
            Close();
            Dispose();
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
                _readEventArgs.UserToken = null;
                _sendEventArgs.UserToken = null;
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}

