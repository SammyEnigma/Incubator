using Incubator.Network;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Incubator.SocketClient
{
    public class StreamedSocketClientConnection : StreamedSocketConnection
    {
        bool _debug;
        bool _disposed;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        IPEndPoint _remoteEndPoint;

        public StreamedSocketClientConnection(string address, int port, int bufferSize, bool debug = false)
            : base(1, new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), debug)
        {
            _debug = debug;
            _disposed = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            _readEventArgs = new SocketAsyncEventArgs();
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _readAwait = new SocketAwaitable(_readEventArgs, null, debug);
            
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _sendAwait = new SocketAwaitable(_sendEventArgs, null, debug);
        }

        public override void Start()
        {
            // just do nothing
        }

        public void Connect()
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
                _readAwait.Dispose();
                _sendAwait.Dispose();
                _readEventArgs.UserToken = null;
                _sendEventArgs.UserToken = null;
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}

