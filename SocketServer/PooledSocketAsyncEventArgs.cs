using System;
using System.Net.Sockets;

namespace Incubator.SocketServer
{
    internal sealed class PooledSocketAsyncEventArgs : IPooledWapper
    {
        private bool _disposed;
        private SocketAsyncEventArgs _socketAsyncEvent;
        private ObjectPool<IPooledWapper> _pool;
        public DateTime LastGetTime { set; get; }
        public bool IsDisposed { get { return _disposed; } }
        public SocketAsyncEventArgs SocketAsyncEvent { get { return _socketAsyncEvent; } }

        public PooledSocketAsyncEventArgs(ObjectPool<IPooledWapper> pool, SocketAsyncEventArgs socketAsyncEvent)
        {
            if (socketAsyncEvent == null)
                throw new ArgumentNullException("socketAsyncEvent不能为空");
            if (pool == null)
                throw new ArgumentNullException("pool不能为空");

            _pool = pool;
            _socketAsyncEvent = socketAsyncEvent;
        }

        ~PooledSocketAsyncEventArgs()
        {
            //必须为false
            Dispose(false);
        }

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
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
                    _socketAsyncEvent.Dispose();
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
