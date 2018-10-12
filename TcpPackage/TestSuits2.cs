using System;
using System.Collections.Generic;
using System.Text;

namespace Incubator.TcpPackage
{
    public class TestSuits2
    {
        private static List<byte> raw_data;

        static TestSuits2()
        {
            raw_data = new List<byte>();
            var body = "login|123456";
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            raw_data.AddRange(head_bytes);
            raw_data.AddRange(body_bytes);
        }

        private static void ResetData()
        {
            raw_data = new List<byte>();
            var body = "login|123456";
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            raw_data.AddRange(head_bytes);
            raw_data.AddRange(body_bytes);
        }

        public static void FunA()
        {
            var gen = new AGenerator(raw_data);
            Parser2.ReadFullyWithPrefix(gen);
            ResetData();
        }

        public static void FunB()
        {
            var gen = new BGenerator(raw_data);
            Parser2.ReadFullyWithPrefix(gen);
            ResetData();
        }

        public static void FunC()
        {
            var gen = new CGenerator(raw_data);
            Parser2.ReadFullyWithPrefix(gen);
            ResetData();
        }

        public static void FunD()
        {
            var gen = new DGenerator(raw_data);
            Parser2.ReadFullyWithPrefix(gen);
            ResetData();
        }

        public static void FunE()
        {
            var gen = new EGenerator(raw_data);
            Parser2.ReadFullyWithPrefix(gen);
            ResetData();
        }
    }
}
