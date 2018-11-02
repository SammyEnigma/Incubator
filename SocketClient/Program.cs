using Incubator.RpcContract;
using Incubator.SocketClient.Rpc;
using System;
using System.Net;

namespace Incubator.SocketClient
{
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

            var proxy = TcpProxy.CreateProxy<IDataContract>(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5000));
            var s = proxy.AddMoney(1, 2);
            Console.WriteLine(s);

            Console.Read();
        }
    }
}
