using System.Collections.Immutable;

using DistIL.AsmIO;
using DistIL.IR;

public class BasicBlockTests
{
    [Fact]
    public void TestInstInserts()
    {
        var method = Utils.CreateDummyMethodBody(PrimType.Int32);
        var block = method.CreateBlock();

        Assert.Null(block.First);
        Assert.Null(block.Last);
        Assert.False(block.GetEnumerator().MoveNext()); //empty
        Assert.Empty(block.NonPhis()); //empty
        Assert.Empty(block.Phis()); //empty

        var inst1 = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(123), ConstInt.CreateI(456));
        var inst2 = new BinaryInst(BinaryOp.Mul, inst1, ConstInt.CreateI(4));
        var inst3 = new ReturnInst(inst2);
        block.InsertFirst(inst1);
        block.InsertLast(inst2);
        block.InsertLast(inst3);
        Assert.True(block.First == inst1 && block.Last == inst3);

        Assert.True(inst1.Prev == null && inst1.Next == inst2);
        Assert.True(inst2.Prev == inst1 && inst2.Next == inst3);
        Assert.True(inst3.Prev == inst2 && inst3.Next == null);

        var inst4 = new BinaryInst(BinaryOp.Mul, inst2, inst1);
        block.InsertAfter(inst2, inst4);
        Assert.True(inst4.Prev == inst2 && inst4.Next == inst3);

        var inst5 = new BinaryInst(BinaryOp.Sub, inst2, inst4);
        block.InsertBefore(inst3, inst5);
        Assert.True(inst5.Prev == inst4 && inst5.Next == inst3);

        block.Remove(inst4);
        Assert.True(inst2.Next == inst5 && inst5.Prev == inst2);
    }

    [Fact]
    public void TestEnumerate()
    {
        var method = Utils.CreateDummyMethodBody(PrimType.Int32);
        var block = method.CreateBlock();
        var inst1 = new PhiInst(PrimType.Int32);
        var inst2 = new PhiInst(PrimType.Int32);
        var inst3 = new BinaryInst(BinaryOp.Add, inst1, inst2);
        var inst4 = new BinaryInst(BinaryOp.Mul, inst3, inst2);
        var inst5 = new ReturnInst(inst4);

        block.InsertLast(inst1);
        block.InsertLast(inst2);
        block.InsertLast(inst3);
        block.InsertLast(inst4);
        block.InsertLast(inst5);

        Assert.Equal(inst1, block.First);
        Assert.Equal(inst5, block.Last);
        Assert.Equal(inst3, block.FirstNonPhi);

        Assert.Equal(new Instruction[] { inst1, inst2, inst3, inst4, inst5 }, ToList(block.GetEnumerator()));
        Assert.Equal(new Instruction[] { inst3, inst4, inst5 }, ToList(block.NonPhis().GetEnumerator()));
        Assert.Equal(new Instruction[] { inst1, inst2 }, ToList(block.Phis().GetEnumerator()));
    }

    [Fact]
    public void TestSplit()
    {
        var method = Utils.CreateDummyMethodBody(PrimType.Int32);
        var block1 = method.CreateBlock();
        var block2 = method.CreateBlock();
        var block3 = method.CreateBlock();
        var block4 = method.CreateBlock();
        var inst1 = new PhiInst(PrimType.Int32);
        var inst2 = new PhiInst(PrimType.Int32);
        var inst3 = new CompareInst(CompareOp.Slt, inst1, inst2);
        var inst4 = new BranchInst(inst3, block2, block3);
        block1.InsertLast(inst1);
        block1.InsertLast(inst2);
        block1.InsertLast(inst3);
        block1.InsertLast(inst4);
        block1.Connect(block2);
        block1.Connect(block3);

        var inst5 = new BinaryInst(BinaryOp.Mul, inst1, inst2);
        var inst6 = new BranchInst(block4);
        block2.InsertLast(inst5);
        block2.InsertLast(inst6);
        block2.Connect(block4);

        var inst7 = new BinaryInst(BinaryOp.Add, inst1, inst2);
        var inst8 = new BranchInst(block4);
        block3.InsertLast(inst7);
        block3.InsertLast(inst8);
        block3.Connect(block4);

        var inst9 = new PhiInst((block2, inst5), (block3, inst7));
        var instA = new ReturnInst(inst4);
        block4.InsertLast(inst9);
        block4.InsertLast(instA);

        var newBlock = block1.Split(inst4);
        Assert.True(block1.Last is BranchInst br && br.Then == newBlock);

        Assert.Equal(new[] { newBlock }, block1.Succs);
        Assert.Equal(new[] { newBlock }, block2.Preds);
        Assert.Equal(new[] { newBlock }, block3.Preds);

        Assert.Equal(new[] { block2, block3 }, newBlock.Succs);

        Assert.Equal(inst4, newBlock.First);
        Assert.Equal(inst4, newBlock.Last);
        Assert.Null(inst4.Prev);
    }

    [Fact]
    public void TestInsertRange()
    {
        var method = Utils.CreateDummyMethodBody(PrimType.Int32);
        var block = method.CreateBlock();

        var insts = GetDummyInsts(8);
        block.InsertRange(null, insts[0], insts[3]);
        Assert.Equal(insts[0], block.First);
        Assert.Equal(insts[3], block.Last);
        Assert.Null(block.First.Prev);
        Assert.Null(block.Last.Next);

        block.InsertRange(block.Last, insts[4], insts[7]);
        Assert.Equal(insts[0], block.First);
        Assert.Equal(insts[7], block.Last);
        Assert.Null(block.First.Prev);
        Assert.Null(block.Last.Next);
    }

    [Fact]
    public void TestRemove()
    {
        var method = Utils.CreateDummyMethodBody(PrimType.Int32);
        var block = method.CreateBlock();

        var insts = GetDummyInsts(8);
        block.InsertRange(null, insts[0], insts[7]);

        block.Remove(insts[3]);
        Assert.Equal(insts.Except(new[] { insts[3] }), ToList(block.GetEnumerator()));
    }

    private List<Instruction> GetDummyInsts(int count)
    {
        var insts = new List<Instruction>();
        for (int i = 0; i < count; i++) {
            var inst = new ReturnInst(ConstInt.CreateI(i));
            if (i > 0) {
                inst.Prev = insts[i - 1];
                insts[i - 1].Next = inst;
            }
            insts.Add(inst);
        }
        return insts;
    }

    private List<Instruction> ToList(IEnumerator<Instruction> itr)
    {
        var list = new List<Instruction>();
        while (itr.MoveNext()) list.Add(itr.Current);
        return list;
    }
}