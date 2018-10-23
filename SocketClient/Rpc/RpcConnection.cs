using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;

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

    public class RpcConnection : ClientConnectionBase, IRpcCall
    {
        private object _syncRoot = new object();
        protected Type _serviceType;
        protected BinaryReader _binReader;
        protected BinaryWriter _binWriter;
        private ParameterTransferHelper _parameterTransferHelper = new ParameterTransferHelper();
        private ServiceSyncInfo _syncInfo;

        // keep cached sync info to avoid redundant wire trips
        private static ConcurrentDictionary<Type, ServiceSyncInfo> _syncInfoCache = new ConcurrentDictionary<Type, ServiceSyncInfo>();

        public RpcConnection(string address, int port, int bufferSize, bool debug = false)
            : base(address, port, bufferSize, debug)
        {
        }

        public object[] InvokeMethod(string metaData, params object[] parameters)
        {
            //prevent call to invoke method on more than one thread at a time
            lock (_syncRoot)
            {
                var mdata = metaData.Split('|');

                //write the message type
                _binWriter.Write((int)MessageType.MethodInvocation);

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

                //write service key index
                _binWriter.Write(_syncInfo.ServiceKeyIndex);

                //write the method ident to the server
                _binWriter.Write(ident);

                //send the parameters
                _parameterTransferHelper.SendParameters(_syncInfo.UseCompression,
                    _syncInfo.CompressionThreshold,
                    _binWriter,
                    parameters);

                _binWriter.Flush();

                // Read the result of the invocation.
                MessageType messageType = (MessageType)_binReader.ReadInt32();
                if (messageType == MessageType.UnknownMethod)
                    throw new Exception("Unknown method.");

                object[] outParams;
                outParams = _parameterTransferHelper.ReceiveParameters(_binReader);

                if (messageType == MessageType.ThrowException)
                    throw (Exception)outParams[0];

                return outParams;
            }
        }

        class SyncInterfaceInfo
        {
            public int Type;
            public string FullName;
        }

        public void SyncInterface(Type serviceType)
        {            
            if (!_syncInfoCache.TryGetValue(serviceType, out _syncInfo))
            {
                var obj = new SyncInterfaceInfo { Type = (int)MessageType.SyncInterface, FullName = serviceType.FullName };
                //write the message type                
                //_binWriter.Write((int)MessageType.SyncInterface);
                //_binWriter.Write(serviceType.FullName);
                Send(JsonConvert.SerializeObject(obj));

                //read sync data
                var len = _binReader.ReadInt32();
                //len is zero when AssemblyQualifiedName not same version or not found
                if (len == 0) throw new TypeAccessException("SyncInterface failed. Type or version of type unknown.");
                var bytes = _binReader.ReadBytes(len);

                _syncInfo = bytes.ToDeserializedObject<ServiceSyncInfo>();
                _syncInfoCache.AddOrUpdate(serviceType, _syncInfo, (t, info) => _syncInfo);
            }
        }
    }
}
