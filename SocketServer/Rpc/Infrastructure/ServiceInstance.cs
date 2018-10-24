using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Incubator.SocketServer.Rpc
{
    public class ServiceInstance
    {
        public int KeyIndex { get; set; }
        public Type InterfaceType { get; set; }
        public object SingletonInstance { get; set; }
        public ConcurrentDictionary<int, MethodInfo> InterfaceMethods { get; set; }
        public ConcurrentDictionary<int, bool[]> MethodParametersByRef { get; set; }
        public ServiceSyncInfo ServiceSyncInfo { get; set; }
    }
}