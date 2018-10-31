using Incubator.SocketServer;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
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
        ParameterTransferHelper _parameterTransferHelper;
        // keep cached sync info to avoid redundant wire trips
        private static ConcurrentDictionary<Type, ServiceSyncInfo> _syncInfoCache = new ConcurrentDictionary<Type, ServiceSyncInfo>();

        public RpcClient2(Type serviceType, IPEndPoint endPoint)
        {
            _debug = false;
            _syncRoot = new object();
            _parameterTransferHelper = new ParameterTransferHelper();
            _connectionPool = new ObjectPool<IPooledWapper>(12, 4, pool => new RpcConnection2(pool, endPoint.Address.ToString(), endPoint.Port, 256, _debug));
            SyncInterface(serviceType).Wait();
        }

        public async Task SyncInterface(Type serviceType)
        {
            if (!_syncInfoCache.TryGetValue(serviceType, out _syncInfo))
            {
                using (var conn = ((RpcConnection2)_connectionPool.Get()))
                {
                    var msg = Encoding.UTF8.GetBytes(serviceType.FullName);
                    await conn.Write(msg, 0, msg.Length, false);

                    var len = await conn.ReadInt32();
                    if (len == 0) throw new TypeAccessException("SyncInterface failed. Type or version of type unknown.");
                    var bytes = await conn.ReadBytes(len);
                    _syncInfo = bytes.Array.ToDeserializedObject<ServiceSyncInfo>();
                    _syncInfoCache.AddOrUpdate(serviceType, _syncInfo, (t, info) => _syncInfo);
                }
            }
        }

        public async Task<object[]> InvokeMethod(string metaData, params object[] parameters)
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

            using (var conn = ((RpcConnection2)_connectionPool.Get()))
            {
                conn.Connect();

                // write the message type
                await conn.Write((int)MessageType.MethodInvocation);

                var invoke_info = new InvokeInfo
                {
                    InvokedServiceKey = _syncInfo.ServiceKeyIndex,
                    MethodHashCode = ident,
                    Parameters = parameters
                };
                var bytes = invoke_info.ToSerializedBytes();
                await conn.Write(bytes, 0, bytes.Length, false);

                // Read the result of the invocation.
                MessageType messageType = (MessageType)await conn.ReadInt32();
                if (messageType == MessageType.UnknownMethod)
                    throw new Exception("Unknown method.");

                var retBytes = await conn.ReadBytes(1);
                var retObj = retBytes.Array.ToDeserializedObject<InvokeReturn>();
                object[] outParams = retObj.ReturnParameters;
                if (messageType == MessageType.ThrowException)
                    throw (Exception)outParams[0];

                return outParams;
            }
        }
    }
}
