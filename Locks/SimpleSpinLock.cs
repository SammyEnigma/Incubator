using System.Threading;

namespace Incubator.Locks
{
    public struct SimpleSpinLock
    {
        private int _resource_in_use; // 初始值为0，表示资源没有被占用

        public void Enter()
        {
            SpinWait spinner = new SpinWait();
            while (true)
            {
                if (Interlocked.Exchange(ref _resource_in_use, 1) == 0)
                {
                    // 当前没有其它线程抢占资源
                    return;
                }
                // 黑魔法
                spinner.SpinOnce();
            }
        }

        public void Leave()
        {
            Volatile.Write(ref _resource_in_use, 0);
        }
    }
}
