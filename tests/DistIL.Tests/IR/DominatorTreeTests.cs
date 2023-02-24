namespace DistIL.Tests.Analysis;

using DistIL.Analysis;
using DistIL.IR;

public class DomTreeTests
{
    public static List<object[]> TestData = new() {
        new[] { GetData1() },
        new[] { GetData2() }
    };

    [Theory, MemberData(nameof(TestData))]
    public void TestIDom(Data item)
    {
        var domTree = new DominatorTree(item.Method);

        foreach (var (block, expDom) in item.Blocks.Zip(item.ExpDom)) {
            Assert.Equal(expDom, domTree.IDom(block));
        }
    }

    [Theory, MemberData(nameof(TestData))]
    public void TestDominates(Data item)
    {
        var domTree = new DominatorTree(item.Method);

        foreach (var block1 in item.Blocks) {
            foreach (var block2 in item.Blocks) {
                Assert.Equal(SlowDominates(block1, block2), domTree.Dominates(block1, block2));
            }
        }

        bool SlowDominates(BasicBlock parent, BasicBlock child)
        {
            while (true) {
                if (child == parent) {
                    return true;
                }
                var idom = domTree.IDom(child);
                if (idom == child) {
                    return false; //reached entry node
                }
                child = idom;
            }
        }
    }

    [Theory, MemberData(nameof(TestData))]
    public void TestTraverse(Data item)
    {
        var domTree = new DominatorTree(item.Method);
        var preOrder = new List<BasicBlock>();
        var postOrder = new List<BasicBlock>();
        CalcExpOrder(item.Method.EntryBlock);
        int preIndex = 0, postIndex = 0;

        domTree.Traverse(
            preVisit: block => Assert.Equal(preOrder[preIndex++], block),
            postVisit: block => Assert.Equal(postOrder[postIndex++], block)
        );

        void CalcExpOrder(BasicBlock block)
        {
            preOrder.Add(block);
            foreach (var child in domTree.GetChildren(block)) {
                CalcExpOrder(child);
            }
            postOrder.Add(block);
        }
    }

    private static Data GetData1()
    {
        var method = Utils.CreateDummyMethodBody();
        var b1 = method.CreateBlock();
        var b2 = method.CreateBlock();
        var b3 = method.CreateBlock();
        var b4 = method.CreateBlock();
        var b5 = method.CreateBlock();
        var b6 = method.CreateBlock();

        //      /---------------\
        //1 -> 2 -> 3 --\       |
        //     \ -> 4 -> 5 -> 6 |
        //          \-----------/
        var cond = ConstInt.CreateI(1);
        b1.SetBranch(b2);
        b2.SetBranch(new BranchInst(cond, b3, b4));
        b3.SetBranch(b5);
        b4.SetBranch(new BranchInst(cond, b5, b2));
        b5.SetBranch(b6);

        return new Data() {
            Method = method,
            Blocks = new[] { b1, b2, b3, b4, b5, b6 },
            ExpDom = new[] { b1, b1, b2, b2, b2, b5 }
        };
    }

    private static Data GetData2()
    {
        //Extracted from Test1::LoopBranch1()
        var method = Utils.CreateDummyMethodBody();
        var BB_01 = method.CreateBlock();
        var BB_02 = method.CreateBlock();
        var BB_11 = method.CreateBlock();
        var BB_13 = method.CreateBlock();
        var BB_15 = method.CreateBlock();
        var BB_26 = method.CreateBlock();
        var BB_38 = method.CreateBlock();
        var BB_41 = method.CreateBlock();
        var BB_47 = method.CreateBlock();
        var BB_51 = method.CreateBlock();
        var BB_56 = method.CreateBlock();
        var BB_62 = method.CreateBlock();
        var cond = ConstInt.CreateI(1);
        BB_01.SetBranch(BB_41);
        BB_02.SetBranch(new BranchInst(cond, BB_13, BB_11));
        BB_11.SetBranch(BB_15);
        BB_13.SetBranch(BB_15);
        BB_15.SetBranch(new BranchInst(cond, BB_38, BB_26));
        BB_26.SetBranch(BB_38);
        BB_38.SetBranch(BB_41);
        BB_41.SetBranch(new BranchInst(cond, BB_02, BB_47));
        BB_47.SetBranch(BB_56);
        BB_51.SetBranch(BB_56);
        BB_56.SetBranch(new BranchInst(cond, BB_51, BB_62));
        return new Data() {
            Method = method,
            Blocks = new[] { BB_01, BB_02, BB_11, BB_13, BB_15, BB_26, BB_38, BB_41, BB_47, BB_51, BB_56, BB_62 },
            ExpDom = new[] { BB_01, BB_41, BB_02, BB_02, BB_02, BB_15, BB_15, BB_01, BB_41, BB_56, BB_47, BB_56 }
        };
    }

    public class Data
    {
        public MethodBody Method { get; init; } = null!;
        public BasicBlock[] Blocks { get; init; } = null!;
        public BasicBlock[] ExpDom { get; init; } = null!;
    }
}