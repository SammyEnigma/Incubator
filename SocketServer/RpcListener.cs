using Incubator.SocketServer.Rpc;
using System.Net.Sockets;
using System.Threading;

namespace Incubator.SocketServer
{
    public sealed class RpcListener : StreamedSocketListener
    {
        bool _debug;

        public RpcListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
        }

        protected override void InnerProcessAccept(SocketAsyncEventArgs e)
        {
            Interlocked.Increment(ref _connectedCount);
            RpcConnection connection = new RpcConnection(_connectedCount, e.AcceptSocket, this, _debug);
            connection.Start();
            ConnectionList.TryAdd(_connectedCount, connection);
        }
    }
}
