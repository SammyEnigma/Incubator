using Incubator.Network;
using Incubator.RpcContract;
using System;
using System.Collections.Generic;

namespace Incubator.SocketClient
{
    public class DataContractImpl : IDataContract
    {
        public long AddMoney(long input1, long input2)
        {
            return input1 + input2;
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
    }

    class Program
    {
        static void Main(string[] args)
        {
            //var server = new Server("127.0.0.1", 5000);
            //server.Start();
            //Console.WriteLine("按任意键关闭server...");
            //Console.ReadLine();
            //server.Stop();

            var server = new RpcServer("127.0.0.1", 5000);

            var simpleContract = new DataContractImpl();
            server.AddService<IDataContract>(simpleContract);

            server.Start();

            Console.WriteLine("按任意键关闭server...");
            Console.ReadLine();
            server.Stop();

            Console.Read();
        }
    }
}
