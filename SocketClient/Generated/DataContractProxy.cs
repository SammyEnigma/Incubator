using Incubator.SocketClient.Rpc;
using System;
using System.Net;

namespace Incubator.RpcContract
{
    public class DataContractProxy : IDataContract
	{
		ulong _serviceHash;
		RpcClient2 _client;

		public DataContractProxy(IPEndPoint endPoint)
		{
			_client = new RpcClient2(typeof(IDataContract), endPoint);
			_serviceHash = CalculateHash(typeof(IDataContract).FullName);
		}

		public Int64 AddMoney(Int64 input1, Int64 input2)
		{
			var ret = _client.InvokeMethod(_serviceHash, 1, new object[] { input1, input2 });
			return (Int64)ret[0];
		}

		private ulong CalculateHash(string str)
		{
			var hashedValue = 3074457345618258791ul;
			for (var i = 0; i < str.Length; i++)
			{
				hashedValue += str[i];
				hashedValue *= 3074457345618258799ul;
			}
			return hashedValue;
		}
	}

	
}