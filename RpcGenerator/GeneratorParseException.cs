using System;

namespace RpcGenerator
{
    public class GeneratorParseException : ApplicationException
    {
        public GeneratorParseException(string message)
            : base(message)
        { }

        public override string Message
        {
            get
            {
                return base.Message;
            }
        }
    }
}
