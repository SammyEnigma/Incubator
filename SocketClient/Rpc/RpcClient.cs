using Incubator.SocketServer;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Incubator.SocketClient.Rpc
{
    public interface IRpcCall
    {
        /// <summary>
        /// Invokes the method with the specified parameters.
        /// </summary>
        /// <param name="parameters">Parameters for the method call</param>
        /// <returns>An array of objects containing the return value (index 0) and the parameters used to call
        /// the method, including any marked as "ref" or "out"</returns>
        object[] InvokeMethod(string metaData, params object[] parameters);

        /// <summary>
        /// Channel must implement an interface synchronization method.
        /// This method asks the server for a list of identifiers paired with method
        /// names and -parameter types. This is used when invoking methods server side.
        /// When username and password supplied, zero knowledge encryption is used.
        /// </summary>
        void SyncInterface(Type serviceType);
    }

    public class RpcClient : IRpcCall
    {
        bool _debug;
        object _syncRoot;
        volatile int _received;
        MemoryStream _stream;
        BinaryWriter _binWriter;
        ServiceSyncInfo _syncInfo;
        object[] outParams;
        ObjectPool<IPooledWapper> _connectionPool;
        ParameterTransferHelper _parameterTransferHelper;
        // keep cached sync info to avoid redundant wire trips
        private static ConcurrentDictionary<Type, ServiceSyncInfo> _syncInfoCache = new ConcurrentDictionary<Type, ServiceSyncInfo>();

        public RpcClient(string address, int port, bool debug)
        {
            _debug = debug;
            _syncRoot = new object();
            _stream = new MemoryStream(512);
            _binWriter = new BinaryWriter(_stream, Encoding.UTF8);
            _parameterTransferHelper = new ParameterTransferHelper();
            _connectionPool = new ObjectPool<IPooledWapper>(12, 4, pool => new RpcConnection(pool, address, port, 256, _debug));
        }

        class SyncInterfaceInfo
        {
            public int MessageType;
            // SyncInterface
            public string FullName;
            // MethodInvocation
            public int ServiceKeyIndex;
            public int MethodIdent;
        }

        public void SyncInterface(Type serviceType)
        {
            if (!_syncInfoCache.TryGetValue(serviceType, out _syncInfo))
            {
                using (var conn = ((RpcConnection)_connectionPool.Get()))
                {
                    conn.OnMessageReceived += (sender, e) =>
                    {
                        _syncInfo = e.MessageData.ToDeserializedObject<ServiceSyncInfo>();
                        _syncInfoCache.AddOrUpdate(serviceType, _syncInfo, (t, info) => _syncInfo);
                        _binWriter.Seek(0, SeekOrigin.Begin);
                    };
                    conn.Connect();
                    _binWriter.Write((int)MessageType.SyncInterface);
                    _binWriter.Write(serviceType.FullName);
                    conn.Send(_stream.GetBuffer(), (int)_stream.Position, false);
                }
            }
        }

        public object[] InvokeMethod(string metaData, params object[] parameters)
        {
            //prevent call to invoke method on more than one thread at a time
            lock (_syncRoot)
            {
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
                                if (!mdata[i + 1].Equals(si.ParameterTypes[i].FullName))
                                {
                                    matchingParameterTypes = false;
                                    break;
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

                using (var conn = ((RpcConnection)_connectionPool.Get()))
                {
                    conn.OnMessageReceived += (sender, e) =>
                    {
                        using (MemoryStream stream = new MemoryStream(e.MessageData))
                        using (BinaryReader br = new BinaryReader(stream))
                        {
                            // Read the result of the invocation.
                            MessageType messageType = (MessageType)br.ReadInt32();
                            if (messageType == MessageType.UnknownMethod)
                                throw new Exception("Unknown method.");

                            outParams = _parameterTransferHelper.ReceiveParameters(br);

                            if (messageType == MessageType.ThrowException)
                                throw (Exception)outParams[0];

                            System.Threading.Interlocked.Exchange(ref _received, 1);
                        }
                    };
                    conn.Connect();
                    // write the message type
                    _binWriter.Write((int)MessageType.MethodInvocation);
                    // write service key index
                    _binWriter.Write(_syncInfo.ServiceKeyIndex);
                    // write the method ident to the server
                    _binWriter.Write(ident);
                    // send the parameters
                    _parameterTransferHelper.SendParameters(_syncInfo.UseCompression,
                        _syncInfo.CompressionThreshold,
                        _binWriter,
                        parameters);

                    conn.Send(_stream.GetBuffer(), (int)_stream.Position, false);
                }

                // todo: rpcclient继承了clientbase的异步方式，而rpc调用天然需要同步方式
                // 后期考虑重构掉这种丑陋的异步模拟同步的方式
                System.Threading.SpinWait spinner = new System.Threading.SpinWait();
                while (_received == 0)
                {
                    spinner.SpinOnce();
                }

                return outParams;
            }
        }
    }
}
