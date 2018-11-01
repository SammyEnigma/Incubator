using System;
using System.Net;
using System.Text;

namespace Incubator.Network
{
    public class Server
    {
        int _bufferSize = 256;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;
        SocketListener _listener;

        public Server(string address, int port, bool debug = false)
        {
            _listener = new SocketListener(_maxConnectionCount, _bufferSize, debug);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            Init();
        }

        public void Init()
        {
            _listener.OnConnectionCreated += On_ConnectionCreated;
            _listener.OnConnectionClosed += On_ConnectionClosed;
            _listener.OnConnectionAborted += On_ConnectionAborted;
            _listener.OnMessageReceived += On_MessageReceived;
            _listener.OnMessageSending += On_MessageSending;
            _listener.OnMessageSent += On_MessageSent;
        }

        public void Start()
        {
            ServerStarting();
            _listener.Start(_endPoint);
            ServerStarted();
        }

        public void Stop()
        {
            ServerStopping();
            _listener.Stop();
            ServerStopped();
        }

        private void ServerStarting()
        {
            Console.WriteLine("server开启中...");
        }

        private void ServerStarted()
        {
            Console.WriteLine("server启动成功");
        }

        private void ServerStopping()
        {
            Console.WriteLine("server关闭中...");
        }

        private void ServerStopped()
        {
            Console.WriteLine("server已关闭");
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

        private void On_MessageReceived(object sender, Package e)
        {
            Console.WriteLine("收到客户端消息：" + Encoding.UTF8.GetString(e.MessageData, 0, e.DataLength));
            var response = "go fuck yourself";
            _listener.Send(e.Connection, response);
        }

        private void On_MessageSending(object sender, Package e)
        {
        }

        private void On_MessageSent(object sender, Package e)
        {
        }
    }
}
