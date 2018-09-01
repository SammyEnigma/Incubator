using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Incubator.TcpPackage
{
    public abstract class StreamGenerator<T>
    {
        protected List<T> raw_data;

        public StreamGenerator(List<T> data)
        {
            raw_data = data;
        }

        public abstract IEnumerable<T[]> Generate();
    }

    public class A1Generator : StreamGenerator<byte>
    {
        public A1Generator(List<byte> data)
            : base(data)
        {
        }

        public override IEnumerable<byte[]> Generate()
        {
            // recv > head + message
            var dirty = "dirtydata";
            raw_data.AddRange(Encoding.UTF8.GetBytes(dirty));

            yield return raw_data.ToArray();
        }
    }

    public class A2Generator : StreamGenerator<byte>
    {
        public A2Generator(List<byte> data) 
            : base(data)
        {
        }

        public override IEnumerable<byte[]> Generate()
        {
            // recv < head + message
            yield return raw_data.Take(raw_data.Count - 3).ToArray();

            // remain
            yield return raw_data.Skip(raw_data.Count - 3).ToArray();
        }
    }

    public class A3Generator : StreamGenerator<byte>
    {
        public A3Generator(List<byte> data)
            : base(data)
        {
        }

        public override IEnumerable<byte[]> Generate()
        {
            // recv = head + message
            yield return raw_data.ToArray();
        }
    }

    public class BGenerator : StreamGenerator<byte>
    {
        public BGenerator(List<byte> data) 
            : base(data)
        {
        }

        public override IEnumerable<byte[]> Generate()
        {
            // recv < head
            yield return raw_data.Take(1).ToArray();

            // remain
            yield return raw_data.Skip(1).ToArray();
        }
    }

    public class CGenerator : StreamGenerator<byte>
    {
        public CGenerator(List<byte> data)
            : base(data)
        {
        }

        public override IEnumerable<byte[]> Generate()
        {
            // recv = head
            yield return raw_data.Take(4).ToArray();

            // remain
            yield return raw_data.Skip(4).ToArray();
        }
    }
}
