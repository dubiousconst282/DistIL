
using DistIL.Util;

public class GraphTraversalTests
{
    [Fact]
    public void TestDFS1()
    {
        var a = new Node(1);
        var b = new Node(2);
        var c = new Node(3);
        var d = new Node(4);
        var e = new Node(5);
        var f = new Node(6);

        //      /----------------\
        //a -> b -> c --\        |
        //     \ -> d -> e -> f  |
        //          \------------/
        a.Connect(b);
        b.Connect(c);
        b.Connect(d);
        c.Connect(e);
        d.Connect(e);
        d.Connect(b);
        e.Connect(f);

        var pre = new List<Node>();
        var post = new List<Node>();
        GraphTraversal.DepthFirst(
            a,
            getChildren: n => n.Succs,
            preVisit: n => pre.Add(n),
            postVisit: n => post.Add(n)
        );

        Assert.Equal(new[] { a, b, c, e, f, d }, pre);
        Assert.Equal(new[] { f, e, c, d, b, a }, post);
    }

    class Node
    {
        public List<Node> Succs = new();
        public int Id;

        public Node(int id) => Id = id;

        public void Connect(Node succ)
        {
            Succs.Add(succ);
        }

        public override string ToString() => ((char)('a' + Id - 1)).ToString();
    }
}