using DistIL.IR;
using DistIL.Analysis;

public class DomTreeTests
{
    private static TestItem GetData1()
    {
        var method = Utils.CreateDummyMethodBody();
        var b1 = method.CreateBlock();
        var b2 = method.CreateBlock();
        var b3 = method.CreateBlock();
        var b4 = method.CreateBlock();
        var b5 = method.CreateBlock();
        var b6 = method.CreateBlock();
        b6.InsertFirst(new ReturnInst());

        //      /---------------\
        //1 -> 2 -> 3 --\       |
        //     \ -> 4 -> 5 -> 6 |
        //          \-----------/
        b1.Connect(b2);
        b2.Connect(b3);
        b2.Connect(b4);
        b3.Connect(b5);
        b4.Connect(b5);
        b4.Connect(b2);
        b5.Connect(b6);

        return new TestItem() {
            Method = method,
            Blocks = new[] { b1, b2, b3, b4, b5, b6 },
            ExpDom = new[] { b1, b1, b2, b2, b2, b5 },
            ExpPostDom = new[] { b2, b5, b5, b5, b6, b6 }
        };
    }
    public static List<object[]> TestData = new() {
        new[] { GetData1() }
    };

    [Theory, MemberData(nameof(TestData))]
    public void TestIDom(TestItem item)
    {
        var domTree = new DominatorTree(item.Method);
        var actDom = new List<BasicBlock>();

        foreach (var block in item.Blocks) {
            actDom.Add(domTree.IDom(block));
        }
        Assert.Equal(item.ExpDom, actDom);

        var postDomTree = new DominatorTree(item.Method, true);
        var actPostDom = new List<BasicBlock>();
        foreach (var block in item.Blocks) {
            actPostDom.Add(postDomTree.IDom(block));
        }
        Assert.Equal(item.ExpPostDom, actPostDom);
    }

    public class TestItem
    {
        public MethodBody Method { get; init; } = null!;
        public BasicBlock[] Blocks { get; init; } = null!;
        public BasicBlock[] ExpDom { get; init; } = null!;
        public BasicBlock[] ExpPostDom { get; init; } = null!;
    }
}