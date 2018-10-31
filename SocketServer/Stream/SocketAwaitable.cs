using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    // 说明：
    // 比如task，编译器生成的代码会获取taskawait，剩下OnCompleted的触发可以理解为t.continue；
    // 但是在此处的socketawait全部目的就在于不要产生task对象，所以OnCompleted的触发由谁来完成是个问题；
    // 
    // 目前分析为，OnCompleted不可能自己触发只能由某个动作完成后回调它，这里最合适这个角色的
    // 就是saea挂载的Completed事件；
    //
    // 于是，逻辑流执行的理论顺序应该是m_eventArgs.Completed -》SocketAwaitable.OnCompleted，
    public sealed class SocketAwaitable : INotifyCompletion, IDisposable
    {
        bool _debug;
        bool _disposed;
        BaseListener _listener;
        readonly static Action SENTINEL = () => { };

        internal bool m_wasCompleted;
        internal Action m_continuation;
        internal SocketAsyncEventArgs m_eventArgs;

        
        public SocketAwaitable(SocketAsyncEventArgs eventArgs, bool debug = false)
            : this(eventArgs, null, debug)
        { }

        // todo: listener在这里携带的信息更多想要表达一种线程模型，后期考虑专门抽象一个类型
        public SocketAwaitable(SocketAsyncEventArgs eventArgs, BaseListener listener, bool debug = false)
        {
            if (eventArgs == null)
                throw new ArgumentNullException("eventArgs");
            if (listener == null)
                throw new ArgumentNullException("listener");

            _debug = debug;
            _disposed = false;
            _listener = listener;
            m_eventArgs = eventArgs;
            m_eventArgs.Completed += IO_Completed;
        }

        ~SocketAwaitable()
        {
            Dispose(false);
        }

        internal void Reset()
        {
            m_wasCompleted = false;
            m_continuation = null;
        }

        public SocketAwaitable GetAwaiter() { return this; }

        public bool IsCompleted { get { return m_wasCompleted; } }

        public void OnCompleted(Action continuation)
        {
            if (m_continuation == SENTINEL || 
                Interlocked.CompareExchange(ref m_continuation, continuation, null) == SENTINEL)
            {
                // 此种情况发生概率很小，就不post到自定义的Scheduler上去了
                Task.Run(continuation);
            }
        }

        public void GetResult()
        {
            if (m_eventArgs.SocketError != SocketError.Success)
                throw new SocketException((int)m_eventArgs.SocketError);
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            var prev = m_continuation ?? Interlocked.CompareExchange(
                    ref m_continuation, SENTINEL, null);
            if (prev != null)
            {
                if (_listener == null)
                {
                    prev();
                }
                else
                {
                    Task.Factory.StartNew(() =>
                    {
                        prev();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    _listener.Scheduler);
                }
            }
        }

        private void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                m_eventArgs.Completed -= IO_Completed;
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
