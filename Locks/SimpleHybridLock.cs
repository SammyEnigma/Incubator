using System.Threading;

namespace Incubator.Locks
{
    public class SimpleHybridLock
    {
        private int _wait;
        private AutoResetEvent _waitlock;
        private int _spincount;

        public SimpleHybridLock()
        {
            _wait = 0;
            _spincount = 4000;
            _waitlock = new AutoResetEvent(false);
        }

        public void Enter()
        {
            SpinWait spinner = new SpinWait();
            for (int i = 0; i < _spincount; i++)
            {
                if (Interlocked.CompareExchange(ref _wait, 1, 0) == 0)
                {
                    // 当前没有其它线程抢占资源
                    return;
                }

                // 黑魔法
                spinner.SpinOnce();
            }

            if (Interlocked.Increment(ref _wait) == 1)
            {
                return;
            }

            // 自旋获取锁失败，开始等待AutoResetEvent
            _waitlock.WaitOne();
        }

        public void Leave()
        {
            if (Interlocked.Decrement(ref _wait) == 0)
            {
                return;
            }

            _waitlock.Set();
        }
    }
}
