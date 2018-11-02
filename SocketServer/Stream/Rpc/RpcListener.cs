using System.Net.Sockets;

namespace Incubator.Network
{
    public sealed class RpcListener : BaseListener
    {
        bool _debug;
        RpcServer _server;

        public RpcListener(int maxConnectionCount, int bufferSize, RpcServer server, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
            _server = server;
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new RpcConnection(_connectedCount, _server, e.AcceptSocket, this, _debug);
        }
    }
}
