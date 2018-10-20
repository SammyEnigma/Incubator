using System;
using System.Buffers;
using System.Net;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer.Client
{
    public class TcpClient : IConnectionEvents
    {
        SocketConnection connection;
        ManualResetEventSlim _shutdownEvent;
        #region 事件
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler<byte[]> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion

        public TcpClient(string address, int port, int bufferSize, bool debug = false)
        {
            
        }

        public void Connect(IPEndPoint localEndPoint)
        {

        }

        public void Stop()
        {
            _shutdownEvent.Set();
            Dispose();
        }

        public void Send(Package package)
        {

        }

        public byte[] GetMessageBytes(string message)
        {
            var body = message;
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            var bytes = ArrayPool<byte>.Shared.Rent(head_bytes.Length + body_bytes.Length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);

            return bytes;
        }

        private void ConnectionClosed(object sender, ConnectionInfo e)
        {
        }

        private void MessageReceived(object sender, byte[] e)
        {
        }

        private void Dispose()
        {
        }
    }
}
