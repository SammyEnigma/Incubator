using System;

namespace Incubator.SocketServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new Server("127.0.0.1", 5000);
            server.Start();
            Console.WriteLine("按任意键关闭server...");
            Console.ReadLine();
            server.Stop();
            Console.Read();
        }
    }
}
