﻿using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Incubator.Network
{
    public sealed class RpcConnection : StreamedSocketConnection
    {
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
                await ProcessInvocation();
            }
        }

        private async Task ProcessInvocation()
        {
            // 读取调用信息
            var obj = await ReadObject<InvokeInfo>();

            // 准备调用方法
            ServiceInfo invokedInstance;
            if ( _server.Services.TryGetValue(obj.ServiceHash, out invokedInstance))
            {
                int index = obj.MethodIndex; 
                object[] parameters = obj.Parameters;

                //invoke the method
                object[] returnParameters;
                var returnMessageType = MessageType.ReturnValues;
                try
                {
                    object returnValue = invokedInstance.Methods[index].Invoke(invokedInstance.Instance, parameters);
                    //the result to the client is the return value (null if void) and the input parameters
                    returnParameters = new object[1 + parameters.Length];
                    returnParameters[0] = returnValue;
                }
                catch (Exception ex)
                {
                    //an exception was caught. Rethrow it client side
                    returnParameters = new object[] { ex };
                    returnMessageType = MessageType.ThrowException;
                }

                var returnObj = new InvokeReturn
                {
                    ReturnType = (int)returnMessageType,
                    ReturnParameters = returnParameters
                };
                //send the result back to the client
                // (2) write the return parameters
                await Write(returnObj);
            }
            else
                await Write(new InvokeReturn { ReturnType = (int)MessageType.UnknownMethod });
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
