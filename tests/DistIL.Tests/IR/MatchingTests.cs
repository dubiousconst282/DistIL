namespace DistIL.Tests.IR;

using DistIL.IR;

public class MatchingTests
{
    [Fact]
    public void TestMatch()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        BinaryInst? instr = null;
        Assert.True(inst.Match($"(add 42 {instr})"));
        Assert.IsType<BinaryInst>(instr);
        Assert.Equal(BinaryOp.Mul, instr.Op);

        ConstInt? x = null;
        Assert.True(inst.Match($"(add {x} (mul ? ?))"));
        Assert.IsType<ConstInt>(x);
        Assert.Equal(42L, x.Value);
    }
}