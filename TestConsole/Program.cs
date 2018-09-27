using Locks.BlockingQueue;
using System;
using System.Threading.Tasks;

namespace TestConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            //var summary = BenchmarkRunner.Run<LockTestSuits>();

            var q = new BlockingQueue2(100);
            var t1 = Task.Run(() =>
            {
                Parallel.For(1, 101, p => q.Enqueue(p));
            }); // 增加100个
            t1.ContinueWith(p => Console.WriteLine(q.Count));

            var t2 = Task.Run(() =>
            {
                Parallel.For(1, 21, p => q.Dequeue());
            }); // 减少20个
            t2.ContinueWith(p => Console.WriteLine(q.Count));

            Console.Read();
        }
    }
}
