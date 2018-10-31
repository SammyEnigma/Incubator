using Incubator.SocketServer;
using System;

namespace Incubator.SocketClient.Rpc
{
    public sealed class RpcConnection : BaseClientConnection, IPooledWapper
    {
        bool _disposed;
        ObjectPool<IPooledWapper> _pool;

        public DateTime LastGetTime { set; get; }
        public bool IsDisposed { get { return _disposed; } }
        public event EventHandler<Package> OnMessageReceived;

        public RpcConnection(ObjectPool<IPooledWapper> pool, string address, int port, int bufferSize, bool debug = false)
            : base(address, port, bufferSize, debug)
        {
            if (pool == null)
                throw new ArgumentNullException("pool");

            _disposed = false;
            _pool = pool;
        }

        ~RpcConnection()
        {
            Dispose(false);
        }

        protected override void MessageReceived(byte[] messageData, int length)
        {
            OnMessageReceived?.Invoke(this, new Package { Connection = null, MessageData = messageData, DataLength = length });
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
                if (_pool.IsDisposed)
                {
                    base.Dispose();
                }
                else
                {
                    _pool.Put(this);
                }
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
