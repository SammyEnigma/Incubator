using System;
using System.IO;
using System.Linq;

namespace Incubator.TcpPackage
{
    public class Parser
    {
        public static byte[] ReadFullyWithPrefix(StreamGenerator<byte> generator)
        {
            byte[] buffer = null;
            byte[] byteArrayForPrefix = null;
            byte[] dataMessageReceived = null;
            int read = 0;
            int messageLength = 0;
            int recPrefixBytesDoneThisOp = 0;
            int receivedPrefixBytesDoneCount = 0;
            int receivedMessageBytesDoneCount = 0;
            while (true)
            {
                foreach (var item in generator.Generate())
                {
                    buffer = new byte[1024];
                    recPrefixBytesDoneThisOp = 0;

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
                    if (receivedPrefixBytesDoneCount < 4) // 包头暂定长度为4byte
                    {
                        remainingBytesToProcess = HandlePrefix(
                            ref receivedPrefixBytesDoneCount,
                            ref recPrefixBytesDoneThisOp,
                            remainingBytesToProcess,
                            ref messageLength,
                            buffer,
                            ref byteArrayForPrefix);

                        if (remainingBytesToProcess == 0)
                        {
                            continue;
                        }
                    }

                    // 能走到这里，情况一定是消息体头部已经完全读完，且，含有多余的部分待处理
                    bool incomingTcpMessageIsReady = HandleMessage(
                        ref receivedMessageBytesDoneCount,
                        recPrefixBytesDoneThisOp,
                        messageLength,
                        ref dataMessageReceived,
                        buffer,
                        remainingBytesToProcess);
                    if (incomingTcpMessageIsReady == true)
                    {
                        return dataMessageReceived;
                    }
                    else
                    {
                        continue;
                    }
                }
            }
        }

        private static bool HandleMessage(ref int receivedMessageBytesDoneCount, int recPrefixBytesDoneThisOp, int messageLength, ref byte[] dataMessageReceived, byte[] buffer, int remainingBytesToProcess)
        {
            bool incomingTcpMessageIsReady = false;
            if (receivedMessageBytesDoneCount == 0)
            {
                dataMessageReceived = new byte[messageLength];
            }

            if (remainingBytesToProcess >= messageLength - receivedMessageBytesDoneCount)
            {
                Buffer.BlockCopy(
                    buffer,
                    recPrefixBytesDoneThisOp,
                    dataMessageReceived,
                    receivedMessageBytesDoneCount,
                    messageLength - receivedMessageBytesDoneCount); // 剩下的部分 remainingBytesToProcess - messageLength应该作何处理？

                receivedMessageBytesDoneCount += (messageLength - receivedMessageBytesDoneCount);
                incomingTcpMessageIsReady = true;
            }
            else
            {
                Buffer.BlockCopy(
                    buffer,
                    recPrefixBytesDoneThisOp,
                    dataMessageReceived,
                    receivedMessageBytesDoneCount,
                    remainingBytesToProcess);

                receivedMessageBytesDoneCount += remainingBytesToProcess;
                remainingBytesToProcess = 0;
            }

            return incomingTcpMessageIsReady;
        }

        private static int HandlePrefix(ref int receivedPrefixBytesDoneCount, ref int recPrefixBytesDoneThisOp, int remainingBytesToProcess, ref int messageLength, byte[] buffer, ref byte[] byteArrayForPrefix)
        {
            if (receivedPrefixBytesDoneCount == 0)
            {
                byteArrayForPrefix = new byte[4];
            }

            // 整个缓冲区接收到的量 >= 头部还未处理的量
            if (remainingBytesToProcess >= 4 - receivedPrefixBytesDoneCount)
            {
                Buffer.BlockCopy(
                    buffer,
                    0, // 之前是receivedPrefixBytesDoneCount，这里考虑到每次都是新的buffer所以offset都应该从0开始
                    byteArrayForPrefix,
                    receivedPrefixBytesDoneCount, // 头部已经处理了的量，用作byteArrayForPrefix中的offset
                    4 - receivedPrefixBytesDoneCount); // 头部还未处理的量（但是本次操作中已经处理了）

                // 设置剩余量 = old剩余量 - 本次操作中已经处理的量，即(4 - receivedPrefixBytesDoneCount)
                remainingBytesToProcess = remainingBytesToProcess - (4 - receivedPrefixBytesDoneCount);

                recPrefixBytesDoneThisOp = 4 - receivedPrefixBytesDoneCount;

                receivedPrefixBytesDoneCount += recPrefixBytesDoneThisOp;

                messageLength = BitConverter.ToInt32(byteArrayForPrefix, 0);
            }
            // 整个缓冲区接收到的量 < 头部待处理的量
            else
            {
                Buffer.BlockCopy(
                    buffer,
                    0,
                    byteArrayForPrefix,
                    receivedPrefixBytesDoneCount,
                    remainingBytesToProcess);

                recPrefixBytesDoneThisOp = remainingBytesToProcess;
                receivedPrefixBytesDoneCount += recPrefixBytesDoneThisOp;
                remainingBytesToProcess = 0;
            }
            // 当remainingBytesToProcess为0，可能是本次处理完之后缓冲区没有剩下未处理的量了（意即本次收到的整个缓冲区的量刚好等于头部待处理量）
            // 另一种可能就是上面紧接着的整个缓冲区接收到的量小于头部待处理的量
            if (remainingBytesToProcess == 0)
            {
                recPrefixBytesDoneThisOp = 0;
            }

            return remainingBytesToProcess;
        }
    }
}
