using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Incubator.SocketServer
{
    public class ObjectPool<T>
    {
        private int _capacity;
        private volatile int _totalCount;
        private SemaphoreSlim _semslots;
        private ConcurrentBag<T> _objects;
        private Func<T> _objectGenerator;

        public ObjectPool(int capacity, Func<T> objectGenerator)
        {
            if (capacity < 1)
                throw new ArgumentException("capacity不能小于1");
            if(objectGenerator ==null)
                throw new ArgumentNullException("objectGenerator不能为空");

            _capacity = capacity;
            _objects = new ConcurrentBag<T>();
            _semslots = new SemaphoreSlim(capacity);

            T item;
            for (int i = 0; i < capacity; i++)
            {
                item = _objectGenerator();
                this._objects.Add(item);
            }
            _totalCount = _capacity;
        }

        public T Pop()
        {
            T item;
            if (_objects.TryTake(out item))
            {
                Interlocked.Increment(ref _totalCount);
                _semslots.Release();
                return item;
            }
            

            if (_totalCount < _capacity)
                return _objectGenerator();

            return default(T);
        }

        public void Push(T item)
        {
            // ConcurrentBag是支持null对象的，所以这里只能自己判断
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }
            _objects.Add(item);
        }

        public void Dispose()
        {

        }
    }
}
