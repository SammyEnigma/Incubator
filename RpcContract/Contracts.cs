using System;
using System.Collections.Generic;

namespace Incubator.RpcContract
{
    public struct ComplexResponse
    {
        public Guid Id { get; set; }
        public string Label { get; set; }
        public long Quantity { get; set; }
    }

    public interface IDataContract
    {
        // 使用int32的时候，在rpc服务端反射调用时总是会报告32转64类型转换异常，
        // 要么是client序列化时4字节搞成了8字节，要么是server反序列化时4字节搞成了8字节
        // 暂时先用long了
        long AddMoney(long input1, long input2);

        decimal GetDecimal(decimal input);
        bool OutDecimal(decimal val);

        Guid GetId(string source, double weight, int quantity, DateTime dt);
        ComplexResponse Get(Guid id, string label, double weight, long quantity);
        long TestLong(long id1, long id2);
        List<string> GetItems(Guid id);
    }
}
