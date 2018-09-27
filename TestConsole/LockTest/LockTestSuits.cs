using BenchmarkDotNet.Attributes;
using Incubator.Locks;
using System.Runtime.CompilerServices;
using System.Threading;

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
        // 简单的等待锁（基于Semaphore）
        private SimpleWaitWithSemaphoreLock semaphorelock;
        // 互斥锁
        private Mutex mutex;
        // 简单的递归锁
        private RecursiveAutoResetEventLock recursivelock;

        public LockTestSuits()
        {
            _resource = 0;
            sslock = new SimpleSpinLock();
            swlock = new SimpleWaitLock();
            semaphorelock = new SimpleWaitWithSemaphoreLock();
            mutex = new Mutex();
            recursivelock = new RecursiveAutoResetEventLock();
        }

        public void AccessDirectly()
        {
            _resource++;
        }

        public void AccessByMethod()
        {
            M();
        }

        [Benchmark]
        public void AccessBySimpleSpinLock()
        {
            sslock.Enter();
            _resource++;
            sslock.Leave();
        }

        [Benchmark]
        public void AccessBySimpleWaitLock()
        {
            swlock.Enter();
            _resource++;
            swlock.Leave();
        }

        [Benchmark]
        public void AccessBySemaphoreLock()
        {
            semaphorelock.Enter();
            _resource++;
            semaphorelock.Leave();
        }

        [Benchmark]
        public void AccessByMutex()
        {
            mutex.WaitOne();
            _resource++;
            mutex.ReleaseMutex();
        }

        [Benchmark]
        public void AccessByRecursiveLock()
        {
            recursivelock.Enter();
            _resource++;
            recursivelock.Leave();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            swlock.Dispose();
            semaphorelock.Dispose();
            mutex.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void M()
        {
            _resource++;
        }
    }
}
