using Incubator.Network;
using System;
using System.Collections.Concurrent;
using System.Net;

namespace Incubator.SocketClient.Rpc
{
    public class RpcClient2
    {
        bool _debug;
        object _syncRoot;
        static ObjectPool<IPooledWapper> _connectionPool;        
        // keep cached sync info to avoid redundant wire trips
        private static ConcurrentDictionary<Type, ServiceSyncInfo> _syncInfoCache = new ConcurrentDictionary<Type, ServiceSyncInfo>();

        public RpcClient2(Type serviceType, IPEndPoint endPoint)
        {
            var count = 0;
            _debug = false;
            _syncRoot = new object();
            _connectionPool = new ObjectPool<IPooledWapper>(12, 4, pool => new RpcConnection2(pool, ++count, endPoint.Address.ToString(), endPoint.Port, 256, _debug));
        }

        internal object[] InvokeMethod(ulong hash, int index, params object[] parameters)
        {
            using (var conn = (RpcConnection2)_connectionPool.Get())
            {
                conn.Connect();

                var invoke_info = new InvokeInfo
                {
                    ServiceHash = hash,
                    MethodIndex = index,
                    Parameters = parameters
                };
                conn.Write(invoke_info).Wait();

                // Read the result of the invocation.
                var retObj = conn.ReadObject<InvokeReturn>().Result;
                if (retObj.ReturnType == (int)MessageType.UnknownMethod)
                    throw new Exception("Unknown method.");
                if (retObj.ReturnType == (int)MessageType.ThrowException)
                    throw (Exception)retObj.ReturnParameters[0];

                object[] outParams = retObj.ReturnParameters;
                return outParams;
            }
        }
    }
}
