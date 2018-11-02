using Incubator.Network;
using Incubator.RpcContract;
using System;

namespace Incubator.SocketClient
{
    public class DataContractImpl : IDataContract
    {
        public int AddMoney(int input1, int input2)
        {
            return input1 + input2;
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
