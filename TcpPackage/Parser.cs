using System;
using System.IO;

namespace Incubator.TcpPackage
{
    public class Parser
    {
        public static byte[] ReadFullyWithPrefix(StreamGenerator<byte> generator)
        {
            byte[] buffer = null;
            byte[] headBuffer = null;
            byte[] bodyBuffer = null;
            int read = 0;
            int messageLength = 0;
            int prefixBytesDoneCount = 0;
            int prefixBytesDoneThisOp = 0;
            int messageBytesDoneCount = 0;
            while (true)
            {
                foreach (var item in generator.Generate())
                {
                    buffer = new byte[1024];
                    prefixBytesDoneThisOp = 0;

                    var stream = new MemoryStream(item);
                    read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        return new byte[0];
                    }

                    int remainingBytesToProcess = read;
                    // 如果第一轮HandlePrefix执行完，remainingBytesToProcess=0，那么可能：
                    // 1. 刚好第一轮读到完整的消息头部，所以continue之后，下面的if是进不去的，于是跳过，直接开始处理消息体
                    // 2. 第一轮没读够消息头部长度，直接进入下面的if
                    if (prefixBytesDoneCount < 4) // 包头暂定长度为4byte
                    {
                        remainingBytesToProcess = HandlePrefix(
                            ref prefixBytesDoneCount,
                            ref prefixBytesDoneThisOp,
                            remainingBytesToProcess,
                            ref messageLength,
                            buffer,
                            ref headBuffer);

                        if (remainingBytesToProcess == 0)
                        {
                            continue;
                        }
                    }

                    // 能走到这里，情况一定是消息体头部已经完全读完，且，含有多余的部分待处理
                    bool incomingTcpMessageIsReady = HandleMessage(
                        ref messageBytesDoneCount,
                        prefixBytesDoneThisOp,
                        messageLength,
                        buffer,
                        ref bodyBuffer,
                        remainingBytesToProcess);
                    if (incomingTcpMessageIsReady == true)
                    {
                        return bodyBuffer;
                    }
                    else
                    {
                        continue;
                    }
                }
            }
        }

        private static int HandlePrefix(ref int prefixBytesDoneCount, ref int prefixBytesDoneThisOp, int remainingBytesToProcess, ref int messageLength, byte[] buffer, ref byte[] headBuffer)
        {
            if (prefixBytesDoneCount == 0)
            {
                headBuffer = new byte[4];
            }

            // 整个缓冲区接收到的量 >= 头部还未处理的量
            if (remainingBytesToProcess >= 4 - prefixBytesDoneCount)
            {
                Buffer.BlockCopy(
                    buffer,
                    0, // 之前是prefixBytesDoneCount，这里考虑到每次都是新的buffer所以offset都应该从0开始
                    headBuffer,
                    prefixBytesDoneCount, // 头部已经处理了的量，用作headBuffer中的offset
                    4 - prefixBytesDoneCount); // 头部还未处理的量（但是本次操作中已经处理了）

                prefixBytesDoneThisOp = 4 - prefixBytesDoneCount;

                prefixBytesDoneCount += prefixBytesDoneThisOp;

                // 设置剩余量 = old剩余量 - 本次操作中已经处理的量（4 - prefixBytesDoneCount），即prefixBytesDoneThisOp
                remainingBytesToProcess = remainingBytesToProcess - prefixBytesDoneThisOp;

                messageLength = BitConverter.ToInt32(headBuffer, 0);
            }
            // 整个缓冲区接收到的量 < 头部待处理的量
            else
            {
                Buffer.BlockCopy(
                    buffer,
                    0,
                    headBuffer,
                    prefixBytesDoneCount,
                    remainingBytesToProcess);

                prefixBytesDoneThisOp = remainingBytesToProcess;
                prefixBytesDoneCount += prefixBytesDoneThisOp;
                remainingBytesToProcess = 0;
            }

            return remainingBytesToProcess;
        }

        private static bool HandleMessage(ref int messageBytesDoneCount, int prefixBytesDoneThisOp, int messageLength, byte[] buffer, ref byte[] bodyBuffer, int remainingBytesToProcess)
        {
            bool incomingTcpMessageIsReady = false;
            if (messageBytesDoneCount == 0)
            {
                bodyBuffer = new byte[messageLength];
            }

            if (remainingBytesToProcess >= messageLength - messageBytesDoneCount)
            {
                Buffer.BlockCopy(
                    buffer,
                    prefixBytesDoneThisOp,
                    bodyBuffer,
                    messageBytesDoneCount,
                    messageLength - messageBytesDoneCount); // 剩下的部分 remainingBytesToProcess - messageLength应该作何处理？

                messageBytesDoneCount += (messageLength - messageBytesDoneCount);
                incomingTcpMessageIsReady = true;
            }
            else
            {
                Buffer.BlockCopy(
                    buffer,
                    prefixBytesDoneThisOp,
                    bodyBuffer,
                    messageBytesDoneCount,
                    remainingBytesToProcess);

                messageBytesDoneCount += remainingBytesToProcess;
                remainingBytesToProcess = 0;
            }

            return incomingTcpMessageIsReady;
        }
    }
}
