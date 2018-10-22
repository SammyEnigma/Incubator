using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Incubator.SocketServer
{
    public class ObjectPool<T>
    {
        private int _capacity;
        private bool _disposed;
        private SemaphoreSlim _semslots;
        private ConcurrentBag<T> _objects;

        public ObjectPool(int capacity, Func<T> objectGenerator)
        {
            if (capacity < 1)
                throw new ArgumentException("capacity不能小于1");
            if (objectGenerator == null)
                throw new ArgumentNullException("objectGenerator不能为空");

            _capacity = capacity;
            _disposed = false;
            _objects = new ConcurrentBag<T>();
            _semslots = new SemaphoreSlim(capacity);

            T item;
            for (int i = 0; i < capacity; i++)
            {
                item = objectGenerator();
                this._objects.Add(item);
            }
        }

        public T Pop()
        {
            _semslots.Wait();
            T item;
            _objects.TryTake(out item);
            return item;
        }

        public void Push(T item)
        {
            // ConcurrentBag是支持null对象的，所以这里只能自己判断
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            _objects.Add(item);
            _semslots.Release();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semslots.Dispose();
            }
        }
    }
}
