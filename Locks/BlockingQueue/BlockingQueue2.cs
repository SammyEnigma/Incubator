using System;
using System.Collections.Generic;
using System.Threading;

namespace Locks.BlockingQueue
{
    /// <summary>
    /// 采用信号量实现
    /// </summary>
    public class BlockingQueue2 : IDisposable
    {
        private int _maxCount;
        private Queue<int> _queue;
        private SemaphoreSlim _semlock;
        private SemaphoreSlim _semslots;
        private SemaphoreSlim _semitems;

        public BlockingQueue2(int maxCount)
        {
            _maxCount = maxCount;
            _queue = new Queue<int>();
            _semlock = new SemaphoreSlim(1);
            _semslots = new SemaphoreSlim(maxCount);
            _semitems = new SemaphoreSlim(0);
        }

        public int Count => _queue.Count;

        public void Enqueue(int item)
        {
            _semslots.Wait();
            _semlock.Wait();
            _queue.Enqueue(item);
            _semlock.Release();
            _semitems.Release();
        }

        public int Dequeue()
        {
            var ret = 0;
            _semitems.Wait();
            _semlock.Wait();
            ret = _queue.Dequeue();
            _semlock.Release();
            _semslots.Release(); 

            return ret;
        }

        public void Dispose()
        {
            _semlock.Dispose();
            _semslots.Dispose();
            _semitems.Dispose();
        }
    }
}
