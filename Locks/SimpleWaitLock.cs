using System;
using System.Threading;

namespace Incubator.Locks
{
    public class SimpleWaitLock : IDisposable
    {
        private readonly AutoResetEvent _available;

        public SimpleWaitLock()
        {
            _available = new AutoResetEvent(true); // 初始为true表示资源没有被占用
        }

        public void Enter()
        {
            _available.WaitOne();
        }

        public void Leave()
        {
            _available.Set();
        }

        public void Dispose()
        {
            _available.Dispose();
        }
    }
}
