using System.Net.Sockets;

namespace Incubator.Network
{
    public sealed class RpcListener : BaseListener
    {
        bool _debug;

        public RpcListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new RpcConnection(_connectedCount, e.AcceptSocket, this, _debug);
        }
    }
}
