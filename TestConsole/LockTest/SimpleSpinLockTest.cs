using BenchmarkDotNet.Attributes;
using Incubator.Locks;
using System.Runtime.CompilerServices;

namespace TestConsole.LockTest
{
    [ClrJob, CoreJob]
    public class SimpleSpinLockTest
    {
        private int _resource;
        private SimpleSpinLock sslock;

        public SimpleSpinLockTest()
        {
            _resource = 0;
            sslock = new SimpleSpinLock();
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void M()
        {
            _resource++;
        }
    }
}
