using Incubator.SocketServer.Rpc;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Incubator.SocketServer
{
    public class StreamedConnection : IConnection, IDisposable
    {
        const int NOT_STARTED = 1;
        const int STARTED = 2;
        const int SHUTTING_DOWN = 3;
        const int SHUTDOWN = 4;
        volatile int _execStatus;

        int _id;
        bool _debug;
        bool _disposed;
        Socket _socket;
        byte[] _readbuffer;
        byte[] _sendbuffer;
        byte[] _largebuffer;
        BaseListener _socketListener;
        PooledSocketAsyncEventArgs _pooledReadEventArgs;
        PooledSocketAsyncEventArgs _pooledSendEventArgs;
        SocketAsyncEventArgs _readEventArgs;
        SocketAsyncEventArgs _sendEventArgs;
        SocketAwaitable _readAwait;
        SocketAwaitable _sendAwait;
        int _position;

        ConcurrentDictionary<string, int> _serviceKeys;
        ConcurrentDictionary<int, ServiceInstance> _services;
        ParameterTransferHelper _parameterTransferHelper;

        internal int Id { get { return _id; } }

        public StreamedConnection(int id, Socket socket, BaseListener listener, bool debug)
        {
            _id = id;
            _debug = debug;
            _execStatus = NOT_STARTED;
            _socketListener = listener;
            _socket = socket;
            _pooledReadEventArgs = _socketListener.SocketAsyncReceiveEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _readEventArgs = _pooledReadEventArgs.SocketAsyncEvent;

            _pooledSendEventArgs = _socketListener.SocketAsyncSendEventArgsPool.Get() as PooledSocketAsyncEventArgs;
            _sendEventArgs = _pooledSendEventArgs.SocketAsyncEvent;

            _readbuffer = _readEventArgs.Buffer;
            _sendbuffer = _sendEventArgs.Buffer;
            _readAwait = new SocketAwaitable(_readEventArgs);
            _sendAwait = new SocketAwaitable(_sendEventArgs);
            _position = 0;

            _serviceKeys = new ConcurrentDictionary<string, int>();
            _services = new ConcurrentDictionary<int, ServiceInstance>();
            _parameterTransferHelper = new ParameterTransferHelper();
        }

        ~StreamedConnection()
        {
            //必须为false
            Dispose(false);
        }

        public async Task Start()
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

        private async Task ProcessSync()
        {
            var serviceTypeName = "";

            int serviceKey;
            if (_serviceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_services.TryGetValue(serviceKey, out instance))
                {
                    //Create a list of sync infos from the dictionary
                    var syncBytes = instance.ServiceSyncInfo.ToSerializedBytes();
                    await Write(syncBytes, 0, syncBytes.Length, false);
                }
            }
            else
            {
                await Write(0);
            }
        }

        class InvokeInfo
        {
            public ServiceInstance InvokedInstance;
            public int MethodHashCode;
            public object[] Parameters;
        }

        class InvokeReturn
        {
            public int ReturnMessageType;
            public object[] ReturnParameters;
        }

        private async Task ProcessInvocation()
        {
            //read service instance key
            var cat = "unknown";
            var stat = "MethodInvocation";
            var invokedServiceKey = await ReadInt32();
            var body = await ReadBytes(invokedServiceKey);
            var obj = body.Array.ToDeserializedObject<InvokeInfo>();

            ServiceInstance invokedInstance;
            if (_services.TryGetValue(invokedServiceKey, out invokedInstance))
            {
                cat = invokedInstance.InterfaceType.Name;
                //read the method identifier
                //int methodHashCode = await ReadInt32();
                int methodHashCode = obj.MethodHashCode;
                if (invokedInstance.InterfaceMethods.ContainsKey(methodHashCode))
                {
                    MethodInfo method;
                    invokedInstance.InterfaceMethods.TryGetValue(methodHashCode, out method);
                    stat = method.Name;

                    bool[] isByRef;
                    invokedInstance.MethodParametersByRef.TryGetValue(methodHashCode, out isByRef);

                    //read parameter data
                    //object[] parameters = _parameterTransferHelper.ReceiveParameters(binReader);
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
                    // (1) write the message type
                    // (2) write the return parameters
                    var retBytes = returnObj.ToSerializedBytes();
                    await Write(retBytes, 0, retBytes.Length, false);
                }
                else
                    await Write((int)MessageType.UnknownMethod);
            }
            else
                await Write((int)MessageType.UnknownMethod);
        }

        public void Close()
        {
            Interlocked.CompareExchange(ref _execStatus, SHUTTING_DOWN, STARTED);
            // close the socket associated with the client
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch
            {
            }
            _socket.Close();
            Interlocked.CompareExchange(ref _execStatus, SHUTDOWN, SHUTTING_DOWN);
        }

        private void DoClose()
        {
            Close();
            (_socketListener as IInnerCallBack).ConnectionClosed(new ConnectionInfo { Num = this.Id, Description = string.Empty, Time = DateTime.Now });
        }

        private void DoAbort(string reason)
        {
            Close();
            Dispose();
            // todo: 直接被设置到task的result里面了，在listener的线程中抓不到这个异常
            // 类似的其它异常也需要注意这种情况
            throw new ConnectionAbortedException(reason);
        }

        private async Task FillBuffer(int count)
        {
            var read = 0;
            do
            {
                _readEventArgs.SetBuffer(read, count - read);
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    break;
                }
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
            _position = read;
        }

        private async Task FillLargeBuffer(int count)
        {
            var read = 0;
            ReleaseLargeBuffer();
            _largebuffer = ArrayPool<byte>.Shared.Rent(count);
            do
            {
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    break;
                }
                Buffer.BlockCopy(_readEventArgs.Buffer, 0, _largebuffer, read, _readEventArgs.BytesTransferred);
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
        }

        private void ReleaseLargeBuffer()
        {
            if (_largebuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_largebuffer, true);
                _largebuffer = null;
            }
        }

        public async Task<int> ReadInt32()
        {
            await FillBuffer(4);
            return (int)(_readbuffer[0] | _readbuffer[1] << 8 | _readbuffer[2] << 16 | _readbuffer[3] << 24);
        }

        public async Task<ArraySegment<byte>> ReadBytes(int count)
        {
            if (count > _socketListener.BufferSize)
            {
                await FillLargeBuffer(count);
                return _largebuffer;
            }
            else
            {
                await FillBuffer(count);
                return new ArraySegment<byte>(_readbuffer, _position, count);
            }
        }

        public async Task Write(byte[] buffer, int offset, int count, bool rentFromPool)
        {
            if (count > _socketListener.BufferSize)
            {
                var remain = count;
                while (remain > 0)
                {
                    _sendEventArgs.SetBuffer(offset, remain);
                    Buffer.BlockCopy(buffer, 0, _sendEventArgs.Buffer, offset, count);
                    await _socket.SendAsync(_sendAwait);
                }
            }
            else
            {

            }

            if (rentFromPool)
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }

        public async Task Write(bool value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = (byte)(value ? 1 : 0);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(byte value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = value;
            await _socket.SendAsync(_sendAwait);
        }

        private unsafe void UnsafeDoubleBytes(double value)
        {
            ulong TmpValue = *(ulong*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
            _sendEventArgs.Buffer[4] = (byte)(TmpValue >> 32);
            _sendEventArgs.Buffer[5] = (byte)(TmpValue >> 40);
            _sendEventArgs.Buffer[6] = (byte)(TmpValue >> 48);
            _sendEventArgs.Buffer[7] = (byte)(TmpValue >> 56);
        }

        public async Task Write(double value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            UnsafeDoubleBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(short value)
        {
            _sendEventArgs.SetBuffer(0, 2);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(int value)
        {
            _sendEventArgs.SetBuffer(0, 4);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(long value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _sendEventArgs.Buffer[4] = (byte)(value >> 32);
            _sendEventArgs.Buffer[5] = (byte)(value >> 40);
            _sendEventArgs.Buffer[6] = (byte)(value >> 48);
            _sendEventArgs.Buffer[7] = (byte)(value >> 56);
            await _socket.SendAsync(_sendAwait);
        }

        private unsafe void UnsafeFloatBytes(float value)
        {
            uint TmpValue = *(uint*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
        }

        public async Task Write(float value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            UnsafeFloatBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(decimal value)
        {
            throw new NotImplementedException();
        }

        private void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _socket.Dispose();
                _sendEventArgs.UserToken = null;
                _readEventArgs.UserToken = null;
                _socketListener.SocketAsyncSendEventArgsPool.Put(_pooledSendEventArgs);
                _socketListener.SocketAsyncReceiveEventArgsPool.Put(_pooledReadEventArgs);
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
