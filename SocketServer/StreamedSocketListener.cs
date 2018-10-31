using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer
{
    public abstract class StreamedSocketListener : BaseListener
    {
        bool _disposed;
        SemaphoreSlim _acceptedClientsSemaphore;

        public StreamedSocketListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _disposed = false;
            _acceptedClientsSemaphore = new SemaphoreSlim(maxConnectionCount, maxConnectionCount);
        }

        ~StreamedSocketListener()
        {
            Dispose(false);
        }

        public override void Start(IPEndPoint localEndPoint)
        {
            _socket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(localEndPoint);
            _socket.Listen(500);
            StartAccept();
        }

        private void StartAccept(SocketAsyncEventArgs acceptEventArg = null)
        {
            if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
            {
                // 关闭事件触发，退出loop
                return;
            }

            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += Accept_Completed;
            }
            else
            {
                acceptEventArg.AcceptSocket = null;
            }

            _acceptedClientsSemaphore.Wait();
            var willRaiseEvent = _socket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        private void Accept_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (_shutdownEvent.Wait(0)) // 仅检查标志，立即返回
            {
                // 关闭事件触发，退出loop
                return;
            }

            try
            {
                InnerProcessAccept(e);
            }
            catch (SocketException ex)
            {
                Print(ex.Message);
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }

            StartAccept(e);
        }

        protected abstract void InnerProcessAccept(SocketAsyncEventArgs e);

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _acceptedClientsSemaphore.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}
