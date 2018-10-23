using System;
using System.Collections.Generic;
using System.Text;

namespace Incubator.SocketClient.Rpc
{
    public class RpcConnection : ClientConnectionBase
    {
        public RpcConnection(string address, int port, int bufferSize, bool debug = false) 
            : base(address, port, bufferSize, debug)
        {
        }
    }
}
