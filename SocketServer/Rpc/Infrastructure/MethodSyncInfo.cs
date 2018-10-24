using System;

namespace Incubator.SocketServer.Rpc
{
    public class MethodSyncInfo
    {
        public int MethodIdent { get; set; }
        public string MethodName { get; set; }
        public Type[] ParameterTypes { get; set; }
    }
}
