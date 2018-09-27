using System.Collections.Generic;
using System.Threading;

namespace Locks.BlockingQueue
{
    /// <summary>
    /// 采用monitor实现
    /// </summary>
    public class BlockingQueue1
    {
        private int _maxCount;
        private object _lock;
        private Queue<int> _queue;

        public BlockingQueue1(int maxCount)
        {
            _maxCount = maxCount;
            _lock = new object();
            _queue = new Queue<int>();
        }

        public int Count => _queue.Count;

        public void Enqueue(int item)
        {
            Monitor.Enter(_lock);
            while (_queue.Count >= _maxCount)
            {
                Monitor.Wait(_lock);
            }
            _queue.Enqueue(item);
            Monitor.Pulse(_lock);
            Monitor.Exit(_lock);
        }

        public int Dequeue()
        {
            var ret = 0;
            Monitor.Enter(_lock);
            while (_queue.Count == 0)
            {
                Monitor.Wait(_lock);
            }
            ret = _queue.Dequeue();
            Monitor.Pulse(_lock);
            Monitor.Exit(_lock);

            return ret;
        }
    }
}
