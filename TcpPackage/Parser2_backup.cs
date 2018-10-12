using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

/*
namespace Incubator.TcpPackage
{
    public enum ParseEnum
    {
        Receive = 1,
        Process_Head = 2,
        Head_Find = 3,
        Process_Body = 4,
        Body_Find = 5
    }

    /// <summary>
    /// 用有限状态机实现的parser
    /// </summary>
    public class Parser2
    {
        static ParseEnum status = ParseEnum.Receive;
        static byte[] receive = new byte[256];
        static byte[] buffer = ArrayPool<byte>.Shared.Rent(512);
        static Socket socket = null;
        static bool skip_head = false;
        static int max_message_length = 512;
        static int head_length = 4;
        static int message_length = 0;
        static int remain = 0;
        static int offset = 0;
        static int recv = 0;
        static int read = 0;

        public static void ReadFullyWithPrefix(StreamGenerator<byte> generator)
        {
            foreach (var item in generator.Generate())
            {
                Array.Clear(receive, 0, receive.Length);
                while (true)
                {
                    switch (status)
                    {
                        case ParseEnum.Receive:
                            {
                                //recv += socket.Receive(receive);
                                var stream = new MemoryStream(item);
                                read = stream.Read(receive, 0, receive.Length);

                                // 接收到FIN
                                if (read == 0)
                                {
                                    return;
                                }
                                recv += read;
                                if (skip_head)
                                {
                                    status = ParseEnum.Process_Body;
                                }
                                else
                                {
                                    status = ParseEnum.Process_Head;
                                }
                            }
                            break;
                        case ParseEnum.Process_Head:
                            {
                                if (recv < head_length)
                                {
                                    if (offset > 0)
                                    {
                                        Buffer.BlockCopy(receive, offset, buffer, 0, read);
                                    }
                                    else
                                    {
                                        Buffer.BlockCopy(receive, 0, buffer, recv - read, read);
                                    }
                                    status = ParseEnum.Receive;
                                    goto Loop;
                                }
                                else
                                {
                                    if (offset > 0)
                                    {
                                        Buffer.BlockCopy(receive, offset, buffer, 0, read);
                                        message_length = BitConverter.ToInt32(buffer, offset);
                                    }
                                    else
                                    {
                                        Buffer.BlockCopy(receive, 0, buffer, recv - read, head_length - (recv - read));
                                        message_length = BitConverter.ToInt32(buffer, 0);
                                    }

                                    if (message_length > max_message_length)
                                    {
                                        Console.WriteLine("消息体长度过大，直接丢弃");
                                        recv = 0;
                                        remain = 0;
                                        message_length = 0;
                                        skip_head = false;
                                        status = ParseEnum.Receive;
                                        goto Loop;
                                    }
                                    else
                                    {
                                        remain = recv - head_length;
                                        status = ParseEnum.Head_Find;
                                    }
                                }
                            }
                            break;
                        case ParseEnum.Head_Find:
                            {
                                if (remain == 0)
                                {
                                    read = 0;
                                    recv = 0;
                                }
                                else
                                {
                                    read = remain;
                                    recv = remain;
                                }

                                status = ParseEnum.Process_Body;
                            }
                            break;
                        case ParseEnum.Process_Body:
                            {
                                if (recv < message_length)
                                {
                                    if (read != 0)
                                    {
                                        Buffer.BlockCopy(receive, offset, buffer, 0, read);
                                    }
                                    skip_head = true;
                                    offset = 0;
                                    status = ParseEnum.Receive;
                                    goto Loop;
                                }
                                else
                                {
                                    Buffer.BlockCopy(receive, skip_head ? 0 : head_length, buffer, recv - read, message_length - (recv - read));
                                    remain = read - (message_length - (recv - read));
                                    status = ParseEnum.Body_Find;
                                }
                            }
                            break;
                        case ParseEnum.Body_Find:
                            {
                                // 派发到工作线程池上执行，避免阻塞主循环
                                Task.Run(() => ProcessMessage(buffer, generator));

                                buffer = ArrayPool<byte>.Shared.Rent(512);
                                if (remain == 0)
                                {
                                    recv = 0;
                                    message_length = 0;
                                    skip_head = false;
                                    status = ParseEnum.Receive;
                                    goto Loop;
                                }
                                else
                                {
                                    recv = remain;
                                    read = remain;
                                    offset = head_length + message_length;
                                    remain = 0;
                                    message_length = 0;
                                    status = ParseEnum.Process_Head;
                                }
                            }
                            break;
                    }
                }
                Loop:;
            }
        }

        static void ProcessMessage(byte[] data, StreamGenerator<byte> generator)
        {
            if (generator is A1Generator)
            {
                Console.WriteLine("=== recv > head + message ===");
                Console.WriteLine("A1 = " + Encoding.UTF8.GetString(data, 0, message_length));
            }
            else if (generator is A2Generator)
            {
                Console.WriteLine("=== recv < head + message ===");
                Console.WriteLine("A2 = " + Encoding.UTF8.GetString(data, 0, message_length));
            }
            else if (generator is A3Generator)
            {
                Console.WriteLine("=== recv = head + message ===");
                Console.WriteLine("A3 = " + Encoding.UTF8.GetString(data, 0, message_length));
            }
            else if (generator is BGenerator)
            {
                Console.WriteLine("=== recv < head ===");
                Console.WriteLine("B = " + Encoding.UTF8.GetString(data, 0, message_length));
            }
            else
            {
                Console.WriteLine("=== recv = head ===");
                Console.WriteLine("C = " + Encoding.UTF8.GetString(data, 0, message_length));
            }

            // 回收当前buffer
            ArrayPool<byte>.Shared.Return(data);
        }
    }
}
*/
