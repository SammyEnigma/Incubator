using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Incubator.TcpPackage
{
    public enum ParseEnum
    {
        Receive = 1,
        Process_Head = 2,
        Process_Body = 3,
        Find_Body = 4
    }

    /// <summary>
    /// 用有限状态机实现的parser
    /// </summary>
    public class Parser2
    {
        static ParseEnum status = ParseEnum.Receive;
        static byte[] buffer = new byte[256];
        static byte[] headBuffer = null;
        static byte[] bodyBuffer = null;
        static Socket socket = null;
        static int maxMessageLength = 512;
        static int headLength = 4;

        public static void ReadFullyWithPrefix(StreamGenerator<byte> generator)
        {
            int messageLength = 0;
            int offset = 0;
            int prefixBytesDoneCount = 0;
            int prefixBytesDoneThisOp = 0;
            int messageBytesDoneCount = 0;
            int messageBytesDoneThisOp = 0;
            int remainingBytesToProcess = 0;

            foreach (var item in generator.Generate())
            {
                Array.Clear(buffer, 0, buffer.Length);
                while (true)
                {
                    switch (status)
                    {
                        case ParseEnum.Receive:
                            {
                                prefixBytesDoneThisOp = 0;
                                messageBytesDoneThisOp = 0;

                                //recv += socket.Receive(receive);
                                var stream = new MemoryStream(item);
                                var read = stream.Read(buffer, 0, buffer.Length);
                                // 接收到FIN
                                if (read == 0)
                                {
                                    return;
                                }

                                remainingBytesToProcess = read;
                                status = ParseEnum.Process_Head;
                            }
                            break;
                        case ParseEnum.Process_Head:
                            {
                                if (prefixBytesDoneCount < headLength)
                                {
                                    if (prefixBytesDoneCount == 0)
                                    {
                                        headBuffer = ArrayPool<byte>.Shared.Rent(headLength);
                                    }

                                    if (remainingBytesToProcess >= headLength - prefixBytesDoneCount)
                                    {
                                        Buffer.BlockCopy(
                                            buffer,
                                            0 + offset,
                                            headBuffer,
                                            prefixBytesDoneCount,
                                            headLength - prefixBytesDoneCount);

                                        prefixBytesDoneThisOp = headLength - prefixBytesDoneCount;
                                        prefixBytesDoneCount += prefixBytesDoneThisOp;
                                        remainingBytesToProcess = remainingBytesToProcess - prefixBytesDoneThisOp;
                                        messageLength = BitConverter.ToInt32(headBuffer, 0);
                                        ArrayPool<byte>.Shared.Return(headBuffer, true);
                                        if (messageLength > maxMessageLength)
                                        {
                                            Console.WriteLine("消息长度超过最大限制，直接丢弃");
                                            return;
                                        }

                                        status = ParseEnum.Process_Body;
                                    }
                                    else
                                    {
                                        Buffer.BlockCopy(
                                            buffer,
                                            0 + offset,
                                            headBuffer,
                                            prefixBytesDoneCount,
                                            remainingBytesToProcess);

                                        prefixBytesDoneThisOp = remainingBytesToProcess;
                                        prefixBytesDoneCount += prefixBytesDoneThisOp;
                                        remainingBytesToProcess = 0;

                                        offset = 0;

                                        status = ParseEnum.Receive;
                                        goto Loop;
                                    }
                                }
                                else
                                {
                                    status = ParseEnum.Process_Body;
                                }
                            }
                            break;
                        case ParseEnum.Process_Body:
                            {
                                if (messageBytesDoneCount == 0)
                                {
                                    bodyBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                                }

                                if (remainingBytesToProcess >= messageLength - messageBytesDoneCount)
                                {
                                    Buffer.BlockCopy(
                                        buffer,
                                        prefixBytesDoneThisOp + offset,
                                        bodyBuffer,
                                        messageBytesDoneCount,
                                        messageLength - messageBytesDoneCount);

                                    messageBytesDoneThisOp = messageLength - messageBytesDoneCount;
                                    messageBytesDoneCount += messageBytesDoneThisOp;
                                    remainingBytesToProcess = remainingBytesToProcess - messageBytesDoneThisOp;

                                    status = ParseEnum.Find_Body;
                                }
                                else
                                {
                                    Buffer.BlockCopy(
                                        buffer,
                                        prefixBytesDoneThisOp + offset,
                                        bodyBuffer,
                                        messageBytesDoneCount,
                                        remainingBytesToProcess);

                                    messageBytesDoneThisOp = remainingBytesToProcess;
                                    messageBytesDoneCount += messageBytesDoneThisOp;
                                    remainingBytesToProcess = 0;

                                    offset = 0;

                                    status = ParseEnum.Receive;
                                    goto Loop;
                                }
                            }
                            break;
                        case ParseEnum.Find_Body:
                            {
                                ProcessMessage(bodyBuffer, messageLength, generator);
                                if (remainingBytesToProcess == 0)
                                {
                                    messageLength = 0;
                                    prefixBytesDoneCount = 0;
                                    messageBytesDoneCount = 0;
                                    status = ParseEnum.Receive;
                                    goto Loop;
                                }
                                else
                                {
                                    offset += (headLength + messageLength);

                                    messageLength = 0;
                                    prefixBytesDoneCount = 0;
                                    prefixBytesDoneThisOp = 0;
                                    messageBytesDoneCount = 0;
                                    messageBytesDoneThisOp = 0;
                                    status = ParseEnum.Process_Head;
                                }
                            }
                            break;
                    }
                }
                Loop:;
            }
        }

        static void ProcessMessage(byte[] data, int length, StreamGenerator<byte> generator)
        {
            if (generator is AGenerator)
            {
                Console.WriteLine("=== recv < head ===");
                Console.WriteLine("A = " + Encoding.UTF8.GetString(data, 0, length));
                Console.WriteLine();
            }
            else if (generator is BGenerator)
            {
                Console.WriteLine("=== recv = head ===");
                Console.WriteLine("B = " + Encoding.UTF8.GetString(data, 0, length));
                Console.WriteLine();
            }
            else if (generator is CGenerator)
            {
                Console.WriteLine("=== recv < head + message ===");
                Console.WriteLine("C = " + Encoding.UTF8.GetString(data, 0, length));
                Console.WriteLine();
            }
            else if (generator is DGenerator)
            {
                Console.WriteLine("=== recv = head + message ===");
                Console.WriteLine("D = " + Encoding.UTF8.GetString(data, 0, length));
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("=== recv > head + message ===");
                Console.WriteLine("E = " + Encoding.UTF8.GetString(data, 0, length));
                Console.WriteLine();
            }

            // 回收当前buffer
            ArrayPool<byte>.Shared.Return(data, true);
        }
    }
}
