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
        }

        public void Init()
        {

        }

        public void Start()
        {
            _listener.Start(_endPoint);
        }

        public void Stop()
        {
            _listener.Stop();
        }
    }
}
