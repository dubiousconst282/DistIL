using DistIL.IR;

public class ValueTests
{
    [Fact]
    public void Test_UseList_SingleUser()
    {
        var value = new DummyValue(123);
        var inst1 = new BinaryInst(BinaryOp.Add, value, value);

        CheckUses(value, (inst1, 0), (inst1, 1));
    }

    [Fact]
    public void Test_UseList_MultipleUsers()
    {
        var value = new DummyValue(123);
        var inst1 = new BinaryInst(BinaryOp.Add, value, value);
        var inst2 = new BinaryInst(BinaryOp.Sub, value, value);
        var inst3 = new BinaryInst(BinaryOp.Mul, value, value);

        CheckUses(value,
            (inst1, 0), (inst1, 1), 
            (inst2, 0), (inst2, 1),
            (inst3, 0), (inst3, 1)
        );
    }

    [Fact]
    public void Test_UseList_Replace()
    {
        var value1 = new DummyValue(123);
        var value2 = new DummyValue(456);
        var value3 = new DummyValue(789);
        var value4 = ConstInt.CreateI(111);
        var inst1 = new BinaryInst(BinaryOp.Add, value2, value1);
        var inst2 = new BinaryInst(BinaryOp.Mul, value1, value2);

        value2.ReplaceUses(value3);
        CheckUses(value2);
        CheckUses(value3, (inst1, 0), (inst2, 1));
        Assert.Equal(value3, inst1.Left);
        Assert.Equal(value3, inst2.Right);

        //Also check untracked values
        value1.ReplaceUses(value4);
        CheckUses(value1);
        Assert.Equal(value4, inst1.Right);
        Assert.Equal(value4, inst2.Left);
    }

    [Fact]
    public void Test_UseList_Reloc()
    {
        var value1 = new DummyValue(123);
        var value2 = new DummyValue(456);
        var block = new BasicBlock(null!);
        var phi = new PhiInst((block, value1), (block, value1), (block, value1), (block, value2));
        //                      0      1          2      3        4       5          6      7
        CheckUses(value1, (phi, 1), (phi, 3), (phi, 5));
        CheckUses(value2, (phi, 7));

        phi.RemoveArg(0, false);
        CheckUses(value1, (phi, 1), (phi, 3));
        CheckUses(value2, (phi, 5));

        phi.RemoveArg(1, false);
        CheckUses(value1, (phi, 1));
        CheckUses(value2, (phi, 3));
    }

    private void CheckUses(TrackedValue value, params (Instruction, int)[] expUses)
    {
        Assert.Equal(expUses.Length, value.NumUses);

        var userSet = new HashSet<Instruction>();
        foreach (var user in value.Users()) {
            userSet.Add(user);
        }
        Assert.Equal(expUses.DistinctBy(u => u.Item1).Count(), userSet.Count);
        userSet.SymmetricExceptWith(expUses.Select(u => u.Item1));
        Assert.Empty(userSet);

        var useSet = new HashSet<(Instruction, int)>();
        foreach (var use in value.Uses()) {
            useSet.Add(use);
        }
        Assert.Equal(expUses.Length, useSet.Count);
        useSet.SymmetricExceptWith(expUses);
        Assert.Empty(useSet);
    }
}