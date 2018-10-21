using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Incubator.SocketServer.Client
{
    public class TcpClient : ClientBase, IConnectionEvents
    {
        Socket _client;
        ManualResetEventSlim _shutdownEvent;
        int _connectTimeout; // 单位毫秒
        #region 事件
        public event EventHandler<ConnectionInfo> OnConnectionCreated;
        public event EventHandler<ConnectionInfo> OnConnectionClosed;
        public event EventHandler<ConnectionInfo> OnConnectionAborted;
        public event EventHandler<byte[]> OnMessageReceived;
        public event EventHandler<Package> OnMessageSending;
        public event EventHandler<Package> OnMessageSent;
        #endregion

        public TcpClient(string address, int port, int bufferSize, bool debug = false)
            : base(address, port, bufferSize, debug)
        {
        }


    }
}
