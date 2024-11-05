namespace DistIL.Tests.IR;

using DistIL.IR;

public class MatchingTests
{
    [Fact]
    public void TestMatch()
    {
        ConstInt? x = null, y = null;
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), ConstInt.CreateI(2));

        if (inst.Match($"add ({x}, {y})"))
        {
            Console.WriteLine($"x: {x.Value}");
            Console.WriteLine($"y: {y.Value}");
        }
    }
}