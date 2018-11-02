using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;

namespace Incubator.Network
{
    public sealed class RpcConnection : StreamedSocketConnection
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

        bool _disposed;
        RpcListener _listener;
        RpcServer _server;

        public RpcConnection(int id, RpcServer server, Socket socket, RpcListener listener, bool debug)
            : base(id, socket, debug)
        {
            _listener = listener;
            _server = server;

            _readEventArgs = _listener.SocketAsyncReadEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _sendEventArgs = _listener.SocketAsyncSendEventArgsPool.Get() as PooledSocketAsyncEventArgs;

            _readAwait = new SocketAwaitable(_readEventArgs, Scheduler, debug);
            _sendAwait = new SocketAwaitable(_sendEventArgs, Scheduler, debug);
        }

        public override async void Start()
        {
            while (true)
            {
                var messageType = (MessageType)await ReadInt32();
                switch (messageType)
                {
                    case MessageType.SyncInterface:
                        await ProcessSync();
                        break;
                    case MessageType.MethodInvocation:
                        await ProcessInvocation();
                        break;
                }
            }
        }

        private async Task ProcessSync()
        {
            var serviceKey = 0;
            var serviceTypeName = await ReadString();
            if (_server.ServiceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_server.Services.TryGetValue(serviceKey, out instance))
                {
                    await Write(instance.ServiceSyncInfo);
                }
            }
            else
            {
                await Write(new ServiceSyncInfo { ServiceKeyIndex = -1 });
            }
        }

        private async Task ProcessInvocation()
        {
            //read service instance key
            var cat = "unknown";
            var stat = "MethodInvocation";
            var obj = await ReadObject<InvokeInfo>();

            ServiceInstance invokedInstance;
            if ( _server.Services.TryGetValue(obj.InvokedServiceKey, out invokedInstance))
            {
                cat = invokedInstance.InterfaceType.Name;
                //read the method identifier
                int methodHashCode = obj.MethodHashCode;
                if (invokedInstance.InterfaceMethods.ContainsKey(methodHashCode))
                {
                    MethodInfo method;
                    invokedInstance.InterfaceMethods.TryGetValue(methodHashCode, out method);
                    stat = method.Name;

                    bool[] isByRef;
                    invokedInstance.MethodParametersByRef.TryGetValue(methodHashCode, out isByRef);

                    //read parameter data
                    object[] parameters = obj.Parameters;

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

                    var returnObj = new InvokeReturn
                    {
                        ReturnMessageType = (int)returnMessageType,
                        ReturnParameters = returnParameters
                    };
                    //send the result back to the client
                    // (2) write the return parameters
                    await Write(returnObj);
                }
                else
                    await Write(new InvokeReturn { ReturnMessageType = (int)MessageType.UnknownMethod });
            }
            else
                await Write(new InvokeReturn { ReturnMessageType = (int)MessageType.UnknownMethod });
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _readAwait.Dispose();
                _sendAwait.Dispose();
                _readEventArgs.UserToken = null;
                _sendEventArgs.UserToken = null;
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}
