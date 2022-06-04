using DistIL.IR;

public class ValueTests
{
    [Fact]
    public void Test_UseList_SingleUser()
    {
        var value = new DummyValue(123);
        var inst1 = new BinaryInst(BinaryOp.Add, value, value);

        CheckUses(
            value,
            new Instruction[] { inst1 },
            new (Instruction, int)[] { (inst1, 0), (inst1, 1) }
        );
    }

    [Fact]
    public void Test_UseList_MultipleUsers()
    {
        var value = new DummyValue(123);
        var inst1 = new BinaryInst(BinaryOp.Add, value, value);
        var inst2 = new BinaryInst(BinaryOp.Sub, value, value);
        var inst3 = new BinaryInst(BinaryOp.Mul, value, value);

        CheckUses(
            value,
            new Instruction[] { inst1, inst2, inst3 },
            new (Instruction, int)[] {
                (inst1, 0), (inst1, 1), 
                (inst2, 0), (inst2, 1),
                (inst3, 0), (inst3, 1)
            }
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
        CheckUses(value2, new Instruction[0], new (Instruction, int)[0]);
        CheckUses(
            value3, 
            new Instruction[] { inst1, inst2 },
            new (Instruction, int)[] { (inst1, 0), (inst2, 1) }
        );
        Assert.Equal(value3, inst1.Left);
        Assert.Equal(value3, inst2.Right);

        //Also check untracked values
        value1.ReplaceUses(value4);
        CheckUses(value1, new Instruction[0], new (Instruction, int)[0]);
        Assert.Equal(value4, inst1.Right);
        Assert.Equal(value4, inst2.Left);
    }

    private void CheckUses(TrackedValue value, Instruction[] expUsers, (Instruction, int)[] expUses)
    {
        Assert.Equal(expUsers.Length, value.NumUsers);
        Assert.True(value.GetNumUses() == expUses.Length);
        Assert.True(value.IsUsedAtLeast(expUses.Length));
        Assert.False(value.IsUsedAtLeast(expUses.Length + 1));

        var userSet = new HashSet<Instruction>();
        foreach (var user in value.Users()) {
            userSet.Add(user);
        }
        Assert.Equal(expUsers.Length, userSet.Count);
        userSet.SymmetricExceptWith(expUsers);
        Assert.Equal(0, userSet.Count);

        var useSet = new HashSet<(Instruction, int)>();
        foreach (var use in value.Uses()) {
            useSet.Add(use);
        }
        Assert.Equal(expUses.Length, useSet.Count);
        useSet.SymmetricExceptWith(expUses);
        Assert.Equal(0, useSet.Count);
    }
}