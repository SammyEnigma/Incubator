using Incubator.SocketServer.Rpc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

namespace Incubator.SocketServer
{
    public class RpcServer 
    {
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        int _compressionThreshold = 131072; //128KB
        bool _useCompression = false; //default is false
        IPEndPoint _endPoint;
        BaseListener _listener;
        ConcurrentDictionary<string, int> _serviceKeys;
        ConcurrentDictionary<int, ServiceInstance> _services;
        ParameterTransferHelper _parameterTransferHelper;

        public RpcServer(string address, int port)
        {
            _serviceKeys = new ConcurrentDictionary<string, int>();
            _services = new ConcurrentDictionary<int, ServiceInstance>();
            _parameterTransferHelper = new ParameterTransferHelper();
            _listener = new SocketListener(_maxConnectionCount, _bufferSize);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
            Init();
        }

        public void Init()
        {
            (_listener as IConnectionEvents).OnConnectionCreated += On_ConnectionCreated;
            (_listener as IConnectionEvents).OnConnectionClosed += On_ConnectionClosed;
            (_listener as IConnectionEvents).OnConnectionAborted += On_ConnectionAborted;
            (_listener as IConnectionEvents).OnMessageReceived += On_MessageReceived;
        }

        public void Start()
        {
            ServerStarting();
            _listener.Start(_endPoint);
            ServerStarted();
        }

        public void Stop()
        {
            ServerStopping();
            _listener.Stop();
            ServerStopped();
        }

        private void ServerStarting()
        {
            Console.WriteLine("server开启中...");
        }

        private void ServerStarted()
        {
            Console.WriteLine("server启动成功");
        }

        private void ServerStopping()
        {
            Console.WriteLine("server关闭中...");
        }

        private void ServerStopped()
        {
            Console.WriteLine("server已关闭");
        }

        private void On_ConnectionCreated(object sender, ConnectionInfo e)
        {
            Console.WriteLine("新建立连接：" + e);
        }

        private void On_ConnectionClosed(object sender, ConnectionInfo e)
        {
            Console.WriteLine("client主动关闭连接：" + e);
        }

        private void On_ConnectionAborted(object sender, ConnectionInfo e)
        {
            Console.WriteLine("连接被强制终止：" + e);
        }

        /// <summary>
        /// Add this service implementation to the host.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="service">The singleton implementation.</param>
        public void AddService<TService>(TService service) where TService : class
        {
            var serviceType = typeof(TService);
            if (!serviceType.IsInterface)
                throw new ArgumentException("TService must be an interface.", "TService");
            var serviceKey = serviceType.FullName;
            if (_serviceKeys.ContainsKey(serviceKey))
                throw new Exception("Service already added. Only one instance allowed.");

            var keyIndex = _serviceKeys.Count;
            _serviceKeys.TryAdd(serviceKey, keyIndex);
            var instance = CreateMethodMap(keyIndex, serviceType, service);
            _services.TryAdd(keyIndex, instance);
        }

        /// <summary>
        /// Loads all methods from interfaces and assigns an identifier
        /// to each. These are later synchronized with the client.
        /// </summary>
        private ServiceInstance CreateMethodMap(int keyIndex, Type serviceType, object service)
        {
            var instance = new ServiceInstance()
            {
                KeyIndex = keyIndex,
                InterfaceType = serviceType,
                InterfaceMethods = new ConcurrentDictionary<int, MethodInfo>(),
                MethodParametersByRef = new ConcurrentDictionary<int, bool[]>(),
                SingletonInstance = service
            };

            var currentMethodIdent = 0;
            if (serviceType.IsInterface)
            {
                var methodInfos = serviceType.GetMethods();
                foreach (var mi in methodInfos)
                {
                    instance.InterfaceMethods.TryAdd(currentMethodIdent, mi);
                    var parameterInfos = mi.GetParameters();
                    var isByRef = new bool[parameterInfos.Length];
                    for (int i = 0; i < isByRef.Length; i++)
                        isByRef[i] = parameterInfos[i].ParameterType.IsByRef;
                    instance.MethodParametersByRef.TryAdd(currentMethodIdent, isByRef);
                    currentMethodIdent++;
                }
            }

            var interfaces = serviceType.GetInterfaces();
            foreach (var interfaceType in interfaces)
            {
                var methodInfos = interfaceType.GetMethods();
                foreach (var mi in methodInfos)
                {
                    instance.InterfaceMethods.TryAdd(currentMethodIdent, mi);
                    var parameterInfos = mi.GetParameters();
                    var isByRef = new bool[parameterInfos.Length];
                    for (int i = 0; i < isByRef.Length; i++)
                        isByRef[i] = parameterInfos[i].ParameterType.IsByRef;
                    instance.MethodParametersByRef.TryAdd(currentMethodIdent, isByRef);
                    currentMethodIdent++;
                }
            }

            //Create a list of sync infos from the dictionary
            var syncSyncInfos = new List<MethodSyncInfo>();
            foreach (var kvp in instance.InterfaceMethods)
            {
                var parameters = kvp.Value.GetParameters();
                var parameterTypes = new Type[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                    parameterTypes[i] = parameters[i].ParameterType;
                syncSyncInfos.Add(new MethodSyncInfo
                {
                    MethodIdent = kvp.Key,
                    MethodName = kvp.Value.Name,
                    ParameterTypes = parameterTypes
                });
            }

            var serviceSyncInfo = new ServiceSyncInfo
            {
                ServiceKeyIndex = keyIndex,
                CompressionThreshold = _compressionThreshold,
                UseCompression = _useCompression,
                MethodInfos = syncSyncInfos.ToArray()
            };
            instance.ServiceSyncInfo = serviceSyncInfo;
            return instance;
        }

        private void On_MessageReceived(object sender, Package e)
        {
            using (MemoryStream stream = new MemoryStream(e.MessageData))
            using (BinaryReader binReader = new BinaryReader(stream, Encoding.UTF8))
            {
                var messageType = (MessageType)binReader.ReadInt32();
                switch (messageType)
                {
                    case MessageType.SyncInterface:
                        ProcessSync(binReader, e);
                        break;
                    case MessageType.MethodInvocation:
                        ProcessInvocation(binReader, e);
                        break;
                }
            }
        }

        private void ProcessSync(BinaryReader binReader, Package e)
        {
            var serviceTypeName = binReader.ReadString();

            int serviceKey;
            if (_serviceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_services.TryGetValue(serviceKey, out instance))
                {
                    //Create a list of sync infos from the dictionary
                    var syncBytes = instance.ServiceSyncInfo.ToSerializedBytes();
                    _listener.Send(e.Connection, syncBytes, syncBytes.Length, false);
                }
            }
            else
            {
                _listener.Send(e.Connection, BitConverter.GetBytes(0), 4, false);
            }
        }

        private void ProcessInvocation(BinaryReader binReader, Package e)
        {
            //read service instance key
            var cat = "unknown";
            var stat = "MethodInvocation";
            var invokedServiceKey = binReader.ReadInt32();

            ServiceInstance invokedInstance;
            using (MemoryStream stream = new MemoryStream(512))
            using (BinaryWriter binWriter = new BinaryWriter(stream, Encoding.UTF8))
            {
                if (_services.TryGetValue(invokedServiceKey, out invokedInstance))
                {
                    cat = invokedInstance.InterfaceType.Name;
                    //read the method identifier
                    int methodHashCode = binReader.ReadInt32();
                    if (invokedInstance.InterfaceMethods.ContainsKey(methodHashCode))
                    {
                        MethodInfo method;
                        invokedInstance.InterfaceMethods.TryGetValue(methodHashCode, out method);
                        stat = method.Name;

                        bool[] isByRef;
                        invokedInstance.MethodParametersByRef.TryGetValue(methodHashCode, out isByRef);

                        //read parameter data
                        object[] parameters = _parameterTransferHelper.ReceiveParameters(binReader);

                        //invoke the method
                        object[] returnParameters;
                        var returnMessageType = MessageType.ReturnValues;
                        try
                        {
                            object returnValue = method.Invoke(invokedInstance.SingletonInstance, parameters);
                            //the result to the client is the return value (null if void) and the input parameters
                            returnParameters = new object[1 + parameters.Length];
                            returnParameters[0] = returnValue;
                            for (int i = 0; i < parameters.Length; i++)
                                returnParameters[i + 1] = isByRef[i] ? parameters[i] : null;
                        }
                        catch (Exception ex)
                        {
                            //an exception was caught. Rethrow it client side
                            returnParameters = new object[] { ex };
                            returnMessageType = MessageType.ThrowException;
                        }

                        //send the result back to the client
                        // (1) write the message type
                        binWriter.Write((int)returnMessageType);

                        // (2) write the return parameters
                        _parameterTransferHelper.SendParameters(
                            invokedInstance.ServiceSyncInfo.UseCompression,
                            invokedInstance.ServiceSyncInfo.CompressionThreshold,
                            binWriter,
                            returnParameters);
                    }
                    else
                        binWriter.Write((int)MessageType.UnknownMethod);
                }
                else
                    binWriter.Write((int)MessageType.UnknownMethod);

                _listener.Send(e.Connection, stream.GetBuffer(), (int)stream.Position, false);
            }
        }
    }
}
