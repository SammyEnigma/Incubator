using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

namespace Incubator.Network
{
    public class RpcServer 
    {
        bool _debug;
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        int _compressionThreshold = 131072; //128KB
        bool _useCompression = false; //default is false
        IPEndPoint _endPoint;
        BaseListener _listener;
        internal ConcurrentDictionary<string, int> ServiceKeys;
        internal ConcurrentDictionary<int, ServiceInstance> Services;

        public RpcServer(string address, int port, bool debug = false)
        {
            _debug = debug;
            ServiceKeys = new ConcurrentDictionary<string, int>();
            Services = new ConcurrentDictionary<int, ServiceInstance>();
            _listener = new RpcListener(_maxConnectionCount, _bufferSize, this, debug);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
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
            if (ServiceKeys.ContainsKey(serviceKey))
                throw new Exception("Service already added. Only one instance allowed.");

            var keyIndex = ServiceKeys.Count;
            ServiceKeys.TryAdd(serviceKey, keyIndex);
            var instance = CreateMethodMap(keyIndex, serviceType, service);
            Services.TryAdd(keyIndex, instance);
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
    }
}
