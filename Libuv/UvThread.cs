using Incubator.Libuv.Interop;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Incubator.Libuv.Internal
{
    // This class needs a bunch of work to make sure it's thread safe
    public class UvThread : ICriticalNotifyCompletion, IDisposable
    {
        private readonly Thread _thread = new Thread(OnStart)
        {
            Name = "Libuv event loop"
        };
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private readonly WorkQueue<Work> _workQueue = new WorkQueue<Work>();

        private bool _stopping;
        private UvAsyncHandle _postHandle;
        private ILibuvTrace _log;

        public UvThread(ILibuvTrace logger)
        {
            _log = logger;
            WriteReqPool = new WriteReqPool(this, logger);
        }

        public LibuvFunctions Uv { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public WriteReqPool WriteReqPool { get; }

#if DEBUG
        public List<WeakReference> Requests { get; } = new List<WeakReference>();
#endif

        public void Post(Action<object> callback, object state)
        {
            if (_stopping)
            {
                return;
            }

            EnsureStarted();

            var work = new Work
            {
                Callback = callback,
                State = state
            };

            _workQueue.Add(work);

            _postHandle.Send();
        }

        // Awaiter impl
        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;

        public UvThread GetAwaiter() => this;

        public void GetResult()
        {

        }

        private static void OnStart(object state)
        {
            ((UvThread)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new LibuvFunctions();

            Loop = new UvLoopHandle(_log);
            Loop.Init(Uv);

            _postHandle = new UvAsyncHandle(_log);
            _postHandle.Init(Loop, OnPost, null);

            _running.Set();

            Uv.run(Loop, 0);

            _postHandle.Reference();
            _postHandle.Dispose();

            Uv.run(Loop, 0);

            Loop.Dispose();
        }

        private void OnPost()
        {
            foreach (var work in _workQueue.DequeAll())
            {
                work.Callback(work.State);
            }

            if (_stopping)
            {
                WriteReqPool.Dispose();

                _postHandle.Unreference();
            }
        }

        private void EnsureStarted()
        {
            if (!_running.IsSet)
            {
                _thread.Start(this);

                _running.Wait();
            }
        }

        private void Stop()
        {
            if (!_stopping)
            {
                _stopping = true;

                _postHandle.Send();

                _thread.Join();

                // REVIEW: Can you restart the thread?
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void OnCompleted(Action continuation)
        {
            Post(state => ((Action)state)(), continuation);
        }

        public void Dispose()
        {
            Stop();
        }

        private struct Work
        {
            public object State;
            public Action<object> Callback;
        }
    }
}
