using System;
using System.Net;

namespace Incubator.SocketServer
{
    public class Server
    {
        SocketListener _listener;
        int _bufferSize = 256;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;

        public Server(string address, int port)
        {
            _listener = new SocketListener(_maxConnectionCount, _bufferSize);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            Init();
        }

        public void Init()
        {
            _listener.OnServerStarting += On_ServerStarting;
            _listener.OnServerStarted += On_ServerStarted;
            _listener.OnConnectionCreated += On_ConnectionCreated;
            _listener.OnConnectionClosed += On_ConnectionClosed;
            _listener.OnConnectionAborted += On_ConnectionAborted;
            _listener.OnServerStopping += On_ServerStopping;
            _listener.OnServerStopped += On_ServerStopped;
            _listener.OnMessageSending += On_MessageSending;
            _listener.OnMessageSent += On_MessageSent;
        }

        public void Start()
        {
            _listener.Start(_endPoint);
        }

        public void Stop()
        {
            _listener.Stop();
        }

        private void On_ServerStarting(object sender, EventArgs e)
        {
            Console.WriteLine("server开启中...");
        }

        private void On_ServerStarted(object sender, EventArgs e)
        {
            Console.WriteLine("server启动成功");
        }

        private void On_ConnectionCreated(object sender, ConnectionInfo e)
        {
            Console.WriteLine("新连接建立成功，Info：" + e);
        }

        private void On_ConnectionClosed(object sender, ConnectionInfo e)
        {
            Console.WriteLine("client主动关闭连接，详情：" + e);
        }

        private void On_ConnectionAborted(object sender, ConnectionInfo e)
        {
            Console.WriteLine("连接被强制终止，详情：" + e);
        }

        private void On_ServerStopping(object sender, EventArgs e)
        {
            Console.WriteLine("server关闭中...");
        }

        private void On_ServerStopped(object sender, EventArgs e)
        {
            Console.WriteLine("server已关闭");
        }

        private void On_MessageSending(object sender, Package e)
        {
            Console.WriteLine("准备发送消息：" + System.Text.Encoding.UTF8.GetString(e.MessageData));
        }

        private void On_MessageSent(object sender, Package e)
        {
            Console.WriteLine("消息发送完毕：" + System.Text.Encoding.UTF8.GetString(e.MessageData));
        }
    }
}
