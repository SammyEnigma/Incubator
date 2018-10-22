﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    public interface IPooledWapper : IDisposable
    {
        DateTime LastGetTime { set; get; }
        bool IsDisposed { get; }
    }

    public sealed class ObjectPool<T> : IDisposable where T : IPooledWapper
    {
        private bool _disposed;
        private int _minRetained;
        private int _maxRetained;
        private SemaphoreSlim _solts;
        private ConcurrentBag<T> _objects;
        private Func<ObjectPool<T>, T> _objectGenerator;
        public bool IsDisposed { get { return _disposed; } }

        public ObjectPool(int maxRetained, int minRetained, Func<ObjectPool<T>, T> objectGenerator)
        {
            if (objectGenerator == null)
                throw new ArgumentNullException("objectGenerator不能为空");
            if (maxRetained < 1)
                throw new ArgumentException("maxRetained不能为负");
            if (minRetained < 1)
                throw new ArgumentException("minRetained不能为负");
            if (maxRetained < minRetained)
                throw new ArgumentException("maxRetained不能小于minRetained");

            _disposed = false;
            _minRetained = minRetained;
            _maxRetained = maxRetained;
            _objectGenerator = objectGenerator;
            _objects = new ConcurrentBag<T>();
            _solts = new SemaphoreSlim(_maxRetained, _maxRetained);

            // 预先初始化
            if (_minRetained > 0)
            {
                Parallel.For(0, _minRetained, i => _objects.Add(_objectGenerator(this)));
            }
        }

        ~ObjectPool()
        {
            //必须为false
            Dispose(false);
        }

        public T Get()
        {
            _solts.Wait();
            T item;
            if (!_objects.TryTake(out item))
            {
                item = _objectGenerator(this);
            }
            return item;
        }

        public void Put(T item)
        {
            // ConcurrentBag是支持null对象的，所以这里只能自己判断
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            _objects.Add(item);
            _solts.Release();
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
                foreach (var item in _objects)
                {
                    item.Dispose();
                }
                _solts.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
