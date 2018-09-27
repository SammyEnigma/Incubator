using System;
using System.Threading;

namespace Incubator.Locks
{
    public class SimpleWaitWithSemaphoreLock : IDisposable
    {
        private readonly Semaphore _available;

        public SimpleWaitWithSemaphoreLock()
        {
            _available = new Semaphore(1, 1); // 初始为1表示资源没有被占用
        }

        public void Enter()
        {
            _available.WaitOne();
        }

        public void Leave()
        {
            _available.Release(1);
        }

        public void Dispose()
        {
            _available.Dispose();
        }
    }
}
