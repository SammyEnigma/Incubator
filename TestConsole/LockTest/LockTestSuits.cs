using BenchmarkDotNet.Attributes;
using Incubator.Locks;
using System.Runtime.CompilerServices;

namespace TestConsole.LockTest
{
    [ClrJob, CoreJob]
    public class LockTestSuits
    {
        private int _resource;
        // 简单的自旋锁
        private SimpleSpinLock sslock;
        // 简单的等待锁（基于AutoResetEvent）
        private SimpleWaitLock swlock;

        public LockTestSuits()
        {
            _resource = 0;
            sslock = new SimpleSpinLock();
            swlock = new SimpleWaitLock();
        }

        [Benchmark]
        public void AccessDirectly()
        {
            _resource++;
        }

        [Benchmark]
        public void AccessByMethod()
        {
            M();
        }

        [Benchmark]
        public void AccessBySSLock()
        {
            sslock.Enter();
            _resource++;
            sslock.Leave();
        }

        [Benchmark]
        public void AccessBySWLock()
        {
            sslock.Enter();
            _resource++;
            sslock.Leave();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            swlock.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void M()
        {
            _resource++;
        }
    }
}
