using System;
using System.Net;

namespace Incubator.SocketServer
{
    public class Server
    {
        IPEndPoint _endPoint;
        BaseListener _listener;
        int _bufferSize = 256;
        int _maxConnectionCount = 500;

        public Server(string address, int port)
        {
            _listener = new SocketListener(_maxConnectionCount, _bufferSize);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            Init();
        }

        public void Init()
        {
            (_listener as IConnectionEvents).OnServerStarting += On_ServerStarting;
            (_listener as IConnectionEvents).OnServerStarted += On_ServerStarted;
            (_listener as IConnectionEvents).OnConnectionCreated += On_ConnectionCreated;
            (_listener as IConnectionEvents).OnConnectionClosed += On_ConnectionClosed;
            (_listener as IConnectionEvents).OnConnectionAborted += On_ConnectionAborted;
            (_listener as IConnectionEvents).OnServerStopping += On_ServerStopping;
            (_listener as IConnectionEvents).OnServerStopped += On_ServerStopped;
            (_listener as IConnectionEvents).OnMessageReceived += On_MessageReceived;
            (_listener as IConnectionEvents).OnMessageSending += On_MessageSending;
            (_listener as IConnectionEvents).OnMessageSent += On_MessageSent;
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
            Console.WriteLine("新建立连接：" + e);
        }

        private void On_ConnectionClosed(object sender, ConnectionInfo e)
        {
            Console.WriteLine("client主动关闭连接：" + e);
        }

        private void On_ConnectionAborted(object sender, ConnectionInfo e)
        {
            Console.WriteLine("连接被强制终止：" + e);
        }

        private void On_ServerStopping(object sender, EventArgs e)
        {
            Console.WriteLine("server关闭中...");
        }

        private void On_ServerStopped(object sender, EventArgs e)
        {
            Console.WriteLine("server已关闭");
        }

        private void On_MessageReceived(object sender, byte[] e)
        {
            Console.WriteLine("收到客户端消息：" + System.Text.Encoding.UTF8.GetString(e));
            var response = "go fuck yourself";
            _listener.Send(new Package { Connection = sender, MessageData = _listener.GetMessageBytes(response) });
        }

        private void On_MessageSending(object sender, Package e)
        {
        }

        private void On_MessageSent(object sender, Package e)
        {
        }
    }
}
