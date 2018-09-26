using BenchmarkDotNet.Running;
using System;
using TestConsole.LockTest;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<SimpleSpinLockTest>();
            Console.Read();
        }
    }
}
