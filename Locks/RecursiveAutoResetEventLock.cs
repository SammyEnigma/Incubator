using System;
using System.Threading;

namespace Incubator.Locks
{
    /// <summary>
    /// 支持线程所有权判定以及递归获取锁
    /// </summary>
    public class RecursiveAutoResetEventLock : IDisposable
    {
        private readonly AutoResetEvent _available;
        private int _currentThreadId;
        private int _recursiveCount;

        public RecursiveAutoResetEventLock()
        {
            _available = new AutoResetEvent(true); // 初始为true表示资源没有被占用
        }

        public void Enter()
        {
            if (_currentThreadId == Thread.CurrentThread.ManagedThreadId)
            {
                _recursiveCount++;
                return;
            }

            _available.WaitOne();
            _recursiveCount = 1;
            _currentThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public void Leave()
        {
            if (_currentThreadId != Thread.CurrentThread.ManagedThreadId)
                throw new Exception("当前线程当前并不拥有该锁");

            if (--_recursiveCount == 0)
            {
                _currentThreadId = 0;
                _available.Set();
            }
        }

        public void Dispose()
        {
            _available.Dispose();
        }
    }
}
