using Incubator.RpcContract;
using Incubator.SocketClient.Rpc;
using System;
using System.Collections.Generic;
using System.Net;

namespace Incubator.SocketClient
{
    interface IAddMoney
    {
        long AddMoney(long a, long b);
    }

    class Proxy : IDataContract
    {
        ulong _serviceHash;
        RpcClient2 _client;

        public Proxy(IPEndPoint endPoint)
        {
            _client = new RpcClient2(typeof(IDataContract), endPoint);
            _serviceHash = CalculateHash(typeof(IDataContract).FullName);
        }

        public long AddMoney(long a, long b)
        {
            var ret = _client.InvokeMethod(_serviceHash, 1, new object[] { a, b });

            return (long)ret[0];
        }

        public ComplexResponse Get(Guid id, string label, double weight, long quantity)
        {
            throw new NotImplementedException();
        }

        public decimal GetDecimal(decimal input)
        {
            throw new NotImplementedException();
        }

        public Guid GetId(string source, double weight, int quantity, DateTime dt)
        {
            throw new NotImplementedException();
        }

        public List<string> GetItems(Guid id)
        {
            throw new NotImplementedException();
        }

        public bool OutDecimal(decimal val)
        {
            throw new NotImplementedException();
        }

        public long TestLong(long id1, long id2)
        {
            throw new NotImplementedException();
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

    class Program
    {
        static void Main(string[] args)
        {
            //var client = new ClientConnectionBase("127.0.0.1", 5000, 256);
            //client.Connect();
            ////==========
            //var message = "loging|123456";
            //client.Send(message);
            //Console.Read();

            //var proxy = TcpProxy.CreateProxy<IDataContract>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000));
            //var s = proxy.AddMoney(1, 2);
            //Console.WriteLine(proxy.AddMoney(1, 2));
            //Console.WriteLine(proxy.AddMoney(1, 2));
            //Console.WriteLine(proxy.AddMoney(1, 2));
            //Console.WriteLine(s);

            var p = new Proxy(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000));
            var s = p.AddMoney(1, 2);
            Console.WriteLine(s);

            Console.Read();
        }
    }
}
