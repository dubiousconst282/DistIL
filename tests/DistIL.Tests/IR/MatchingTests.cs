namespace DistIL.Tests.IR;

using DistIL.IR;

public class MatchingTests
{
    [Fact]
    public void TestMatch()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Instruction? instr = null;
        if (inst.Match($"(add 42 {instr})")) {
            Console.WriteLine("Right: " + instr);
        }

        ConstInt? x = null;
        if (inst.Match($"(add {x} (mul ? ?))"))
        {
            Console.WriteLine($"x: {x.Value}");
        }
    }
}