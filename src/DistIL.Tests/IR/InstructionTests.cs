using DistIL.IR;

public class InstructionTests
{
    [Fact]
    public void TestValueUseTracking()
    {
        var value = ConstInt.CreateI(123);
        var inst = new BinaryInst(BinaryOp.Add, value, value);

        Assert.Equal(new Use() { Inst = inst, OperandIdx = 0 }, value.Uses[0]);
        Assert.Equal(new Use() { Inst = inst, OperandIdx = 1 }, value.Uses[1]);

        var value2 = ConstInt.CreateI(256);
        value.ReplaceUses(value2);

        Assert.Empty(value.Uses);
        Assert.Equal(new Use() { Inst = inst, OperandIdx = 0 }, value2.Uses[0]);
        Assert.Equal(new Use() { Inst = inst, OperandIdx = 1 }, value2.Uses[1]);
    }
}