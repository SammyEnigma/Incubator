using Incubator.SocketServer.Rpc;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

namespace Incubator.SocketServer
{
    public class RpcServer
    {
        int _bufferSize = 256;
        int _maxConnectionCount = 500;
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
            var syncCat = "Sync";
            var serviceTypeName = binReader.ReadString();

            int serviceKey;
            if (_serviceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_services.TryGetValue(serviceKey, out instance))
                {
                    syncCat = instance.InterfaceType.Name;
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
