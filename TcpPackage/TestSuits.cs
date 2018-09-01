using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Incubator.TcpPackage
{
    public class TestSuits
    {
        private static List<byte> raw_data;
        static TestSuits()
        {
            raw_data = new List<byte>();
            var body = "login|123456#";
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            raw_data.AddRange(head_bytes);
            raw_data.AddRange(body_bytes);
        }

        private static void ResetData()
        {
            raw_data = new List<byte>();
            var body = "login|123456#";
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            raw_data.AddRange(head_bytes);
            raw_data.AddRange(body_bytes);
        }

        // recv > head + message
        public static void FunA_1()
        {
            Console.WriteLine("=== recv > head + message ===");
            var gen = new A1Generator(raw_data);
            var data1 = Parser.ReadFullyWithPrefix(gen);
            var b1 = Encoding.UTF8.GetString(data1, 0, data1.Length);
            Console.WriteLine("b1 = " + b1);
            ResetData();
        }

        // recv < head + message
        public static void FunA_2()
        {
            Console.WriteLine("=== recv < head + message ===");
            var gen = new A2Generator(raw_data);
            var data2 = Parser.ReadFullyWithPrefix(gen);
            var b2 = Encoding.UTF8.GetString(data2, 0, data2.Length);
            Console.WriteLine("b2 = " + b2);
            ResetData();
        }

        // recv = head + message
        public static void FunA_3()
        {
            Console.WriteLine("=== recv = head + message ===");
            var gen = new A3Generator(raw_data);
            var data3 = Parser.ReadFullyWithPrefix(gen);
            var b3 = Encoding.UTF8.GetString(data3, 0, data3.Length);
            Console.WriteLine("b3 = " + b3);
            ResetData();
        }

        // recv < head
        public static void FunB()
        {
            Console.WriteLine("=== recv < head ===");
            var gen = new BGenerator(raw_data);
            var data4 = Parser.ReadFullyWithPrefix(gen);
            var b4 = Encoding.UTF8.GetString(data4, 0, data4.Length);
            Console.WriteLine("b4 = " + b4);
            ResetData();
        }

        // recv = head
        public static void FunC()
        {
            Console.WriteLine("=== recv = head ===");
            var gen = new CGenerator(raw_data);
            var data5 = Parser.ReadFullyWithPrefix(gen);
            var b5 = Encoding.UTF8.GetString(data5, 0, data5.Length);
            Console.WriteLine("b5 = " + b5);
            ResetData();
        }
    }
}
