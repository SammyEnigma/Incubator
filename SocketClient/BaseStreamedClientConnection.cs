using Incubator.SocketServer;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketClient
{
    public abstract class BaseStreamedClientConnection : IDisposable
    {
        bool _debug;
        bool _disposed;
        int _position;
        byte[] _largebuffer;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        Socket _socket;
        IPEndPoint _remoteEndPoint;
        SocketAsyncEventArgs _readEventArgs;
        SocketAsyncEventArgs _sendEventArgs;
        SocketAwaitable _readAwait;
        SocketAwaitable _sendAwait;

        public BaseStreamedClientConnection(string address, int port, int bufferSize, bool debug = false)
        {
            _debug = debug;
            _disposed = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);

            _readEventArgs = new SocketAsyncEventArgs();            
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);

            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);

            _readAwait = new SocketAwaitable(_readEventArgs, debug);
            _sendAwait = new SocketAwaitable(_sendEventArgs, debug);
        }

        ~BaseStreamedClientConnection()
        {
            Dispose(false);
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
            InnerStart();
        }

        protected abstract void InnerStart();

        public void Close()
        {
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

        private async Task FillBuffer(int count)
        {
            var read = 0;
            do
            {
                _readEventArgs.SetBuffer(read, count - read);
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    // todo: 添加处理逻辑
                    break;
                }
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
            _position = read;
        }

        private async Task FillLargeBuffer(int count)
        {
            var read = 0;
            ReleaseLargeBuffer();
            _largebuffer = ArrayPool<byte>.Shared.Rent(count);
            do
            {
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    break;
                }
                Buffer.BlockCopy(_readEventArgs.Buffer, 0, _largebuffer, read, _readEventArgs.BytesTransferred);
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
        }

        private void ReleaseLargeBuffer()
        {
            if (_largebuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_largebuffer, true);
                _largebuffer = null;
            }
        }

        public async Task<int> ReadInt32()
        {
            await FillBuffer(4);
            return (int)(_readEventArgs.Buffer[0] | _readEventArgs.Buffer[1] << 8 | _readEventArgs.Buffer[2] << 16 | _readEventArgs.Buffer[3] << 24);
        }

        public async Task<ArraySegment<byte>> ReadBytes(int count)
        {
            if (count > _readEventArgs.Buffer.Length)
            {
                await FillLargeBuffer(count);
                return _largebuffer;
            }
            else
            {
                await FillBuffer(count);
                return new ArraySegment<byte>(_readEventArgs.Buffer, _position, count);
            }
        }

        public async Task Write(byte[] buffer, int offset, int count, bool rentFromPool)
        {
            var sent = 0;
            var remain = count;
            while (remain > 0)
            {
                Buffer.BlockCopy(buffer, offset + sent, _sendEventArgs.Buffer, 0, remain > _sendEventArgs.Buffer.Length ? _sendEventArgs.Buffer.Length : remain);
                await _socket.SendAsync(_sendAwait);
                sent += _sendEventArgs.BytesTransferred;
                remain -= _sendEventArgs.BytesTransferred;
            }

            if (rentFromPool)
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }

        public async Task Write(bool value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = (byte)(value ? 1 : 0);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(byte value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = value;
            await _socket.SendAsync(_sendAwait);
        }

        private unsafe void UnsafeDoubleBytes(double value)
        {
            ulong TmpValue = *(ulong*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
            _sendEventArgs.Buffer[4] = (byte)(TmpValue >> 32);
            _sendEventArgs.Buffer[5] = (byte)(TmpValue >> 40);
            _sendEventArgs.Buffer[6] = (byte)(TmpValue >> 48);
            _sendEventArgs.Buffer[7] = (byte)(TmpValue >> 56);
        }

        public async Task Write(double value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            UnsafeDoubleBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(short value)
        {
            _sendEventArgs.SetBuffer(0, 2);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(int value)
        {
            _sendEventArgs.SetBuffer(0, 4);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(long value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _sendEventArgs.Buffer[4] = (byte)(value >> 32);
            _sendEventArgs.Buffer[5] = (byte)(value >> 40);
            _sendEventArgs.Buffer[6] = (byte)(value >> 48);
            _sendEventArgs.Buffer[7] = (byte)(value >> 56);
            await _socket.SendAsync(_sendAwait);
        }

        private unsafe void UnsafeFloatBytes(float value)
        {
            uint TmpValue = *(uint*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
        }

        public async Task Write(float value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            UnsafeFloatBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(decimal value)
        {
            throw new NotImplementedException();
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
                _readAwait.Dispose();
                _sendAwait.Dispose();
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}

