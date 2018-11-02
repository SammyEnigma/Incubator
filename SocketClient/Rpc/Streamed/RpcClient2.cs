using Incubator.Network;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace Incubator.SocketClient.Rpc
{
    public class RpcClient2
    {
        class InvokeInfo
        {
            public int InvokedServiceKey;
            public int MethodHashCode;
            public object[] Parameters;
        }

        class InvokeReturn
        {
            public int ReturnMessageType;
            public object[] ReturnParameters;
        }

        bool _debug;
        object _syncRoot;
        ServiceSyncInfo _syncInfo;
        ObjectPool<IPooledWapper> _connectionPool;        
        // keep cached sync info to avoid redundant wire trips
        private static ConcurrentDictionary<Type, ServiceSyncInfo> _syncInfoCache = new ConcurrentDictionary<Type, ServiceSyncInfo>();

        public RpcClient2(Type serviceType, IPEndPoint endPoint)
        {
            var count = 0;
            _debug = false;
            _syncRoot = new object();
            _connectionPool = new ObjectPool<IPooledWapper>(12, 4, pool => new RpcConnection2(pool, ++count, endPoint.Address.ToString(), endPoint.Port, 256, _debug));
            SyncInterface(serviceType).Wait();
        }

        public async Task SyncInterface(Type serviceType)
        {
            if (!_syncInfoCache.TryGetValue(serviceType, out _syncInfo))
            {
                using (var conn = (RpcConnection2)_connectionPool.Get())
                {
                    conn.Connect();

                    await conn.Write((int)MessageType.SyncInterface);

                    await conn.Write(serviceType.FullName);

                    _syncInfo = await conn.ReadObject<ServiceSyncInfo>();
                    if (_syncInfo.ServiceKeyIndex == -1)
                        throw new TypeAccessException("SyncInterface failed. Type or version of type unknown.");
                    _syncInfoCache.AddOrUpdate(serviceType, _syncInfo, (t, info) => _syncInfo);
                }
            }
        }

        public object[] InvokeMethod(string metaData, params object[] parameters)
        {
            //prevent call to invoke method on more than one thread at a time
            var mdata = metaData.Split('|');

            //find the matching server side method ident
            var ident = -1;
            for (int index = 0; index < _syncInfo.MethodInfos.Length; index++)
            {
                var si = _syncInfo.MethodInfos[index];
                //first of all the method names must match
                if (si.MethodName == mdata[0])
                {
                    //second of all the parameter types and -count must match
                    if (mdata.Length - 1 == si.ParameterTypes.Length)
                    {
                        var matchingParameterTypes = true;
                        for (int i = 0; i < si.ParameterTypes.Length; i++)
                        {
                            if (!mdata[i + 1].Equals(si.ParameterTypes[i].FullName))
                            {
                                matchingParameterTypes = false;
                                break;
                            }
                        }
                        if (matchingParameterTypes)
                        {
                            ident = si.MethodIdent;
                            break;
                        }
                    }
                }
            }

            if (ident < 0)
                throw new Exception(string.Format("Cannot match method '{0}' to its server side equivalent", mdata[0]));
            
            using (var conn = (RpcConnection2)_connectionPool.Get())
            {
                conn.Connect();

                // write the message type
                conn.Write((int)MessageType.MethodInvocation).Wait();

                var invoke_info = new InvokeInfo
                {
                    InvokedServiceKey = _syncInfo.ServiceKeyIndex,
                    MethodHashCode = ident,
                    Parameters = parameters
                };
                conn.Write(invoke_info).Wait();

                // Read the result of the invocation.
                var retObj = conn.ReadObject<InvokeReturn>().Result;
                if (retObj.ReturnMessageType == (int)MessageType.UnknownMethod)
                    throw new Exception("Unknown method.");
                if (retObj.ReturnMessageType == (int)MessageType.ThrowException)
                    throw (Exception)retObj.ReturnParameters[0];

                object[] outParams = retObj.ReturnParameters;
                return outParams;
            }
        }
    }
}
