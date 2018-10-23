using System;

namespace Incubator.SocketClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new ClientConnectionBase("127.0.0.1", 5000, 256);
            client.Connect();
            //==========
            var message = "loging|123456";
            client.Send(message);
            Console.Read();
        }
    }
}
