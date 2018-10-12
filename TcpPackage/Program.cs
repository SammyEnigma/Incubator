using System;

namespace Incubator.TcpPackage
{
    class Program
    {
        static void Main(string[] args)
        {
            //TestSuits.FunA();
            //TestSuits.FunB();
            //TestSuits.FunC();
            //TestSuits.FunD();
            //TestSuits.FunE();

            ////=== recv < head ===
            //TestSuits2.FunA();
            ////=== recv = head ===
            //TestSuits2.FunB();
            ////=== recv < head + message ===
            //TestSuits2.FunC();
            ////=== recv = head + message ===
            //TestSuits2.FunD();
            ////=== recv > head + message ===
            TestSuits2.FunE();
            Console.Read();
        }

        static void f(byte[] d)
        {
            System.Threading.Thread.Sleep(3000);

            Console.WriteLine(d[0]);
            Console.WriteLine(d[1]);
            Console.WriteLine(d[2]);
        }
    }
}
