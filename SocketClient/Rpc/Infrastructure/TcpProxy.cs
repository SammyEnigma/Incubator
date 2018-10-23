using System.Net;

namespace Incubator.SocketClient.Rpc
{
    public sealed class TcpProxy
    {
        public static TInterface CreateProxy<TInterface>(IPEndPoint endpoint) where TInterface : class
        {
            return ProxyFactory.CreateProxy<TInterface>(typeof(RpcConnection), typeof(IPEndPoint), endpoint);
        }
    }
}
