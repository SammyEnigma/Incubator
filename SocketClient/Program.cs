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
            var body = "loging|123456";
            var body_bytes = System.Text.Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(head_bytes.Length + body_bytes.Length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);
            //==========

            client.Send(bytes);
            Console.Read();
        }
    }
}
