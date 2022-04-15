namespace DistIL.IR;

public class DominatorTree
{
    readonly Dictionary<BasicBlock, Node> _block2node = new();
    readonly Node _root;

    public Method Method { get; }
    public bool IsPostDom { get; }

    public DominatorTree(Method method, bool isPostDom = false)
    {
        Method = method;
        IsPostDom = isPostDom;

        var nodes = CreateNodes();
        _root = nodes[^1];
        ComputeDom(nodes);
        ComputeChildren(nodes);
    }

    /// <summary> 
    /// Returns the immediate dominator of `block` or itself if it's the entry block, 
    /// or an exit block and this is a post dominator tree.
    /// </summary>
    public BasicBlock IDom(BasicBlock block)
    {
        return GetNode(block).IDom.Block;
    }

    /// <summary> Checks if `parent` block dominates `child`. </summary>
    public bool Dominates(BasicBlock parent, BasicBlock child)
    {
        //TODO: this could be O(1) by using the pre and post dfs index of the dom tree
        var node = GetNode(child);
        while (true) {
            if (node.Block == parent) {
                return true;
            }
            if (node == node.IDom) {
                return false; //reached entry node
            }
            node = node.IDom;
        }
    }

    /// <summary> Performs a depth first traversal over this dominator tree. </summary>
    public void Traverse(Action<BasicBlock>? preVisit = null, Action<BasicBlock>? postVisit = null)
    {
        var emptyList = new List<Node>();

        GraphTraversal.DepthFirst(
            entry: _root,
            getChildren: b => b.Children ?? emptyList,
            preVisit: preVisit == null ? null : n => preVisit(n.Block),
            postVisit: postVisit == null ? null : n => postVisit(n.Block)
        );
    }

    private Node GetNode(BasicBlock block)
    {
        return _block2node[block];
    }

    /// <summary> Creates the tree nodes and returns a list with them in DFS post order. </summary>
    private List<Node> CreateNodes()
    {
        var blocks = new List<Node>();
        var entryBlock = Method.EntryBlock;

        Assert(!entryBlock.Succs.Contains(entryBlock));
        
        if (IsPostDom) {
            //TODO: avoid creating temp block for inverted graph dfs
            entryBlock = new BasicBlock(Method);
            foreach (var block in Method) {
                if (IsExitBlock(block)) {
                    entryBlock.Preds.Add(block);
                }
            }
        }
        GraphTraversal.DepthFirst(
            entry: entryBlock,
            getChildren: IsPostDom ? (b => b.Preds) : (b => b.Succs),
            postVisit: b => {
                var node = new Node() {
                    Block = b,
                    PostIndex = blocks.Count
                };
                _block2node.Add(b, node);
                blocks.Add(node);
            }
        );
        return blocks;
    }

    //Algorithm from the paper "A Simple, Fast Dominance Algorithm"
    //https://www.cs.rice.edu/~keith/EMBED/dom.pdf
    private void ComputeDom(List<Node> nodes)
    {
        var entry = nodes[^1];
        entry.IDom = entry; //entry block dominates itself

        bool changed = true;
        while (changed) {
            changed = false;
            //foreach block in reverse post order, except entry (at `len - 1`)
            for (int i = nodes.Count - 2; i >= 0; i--) {
                var node = nodes[i];
                var block = node.Block;
                var newDom = default(Node);

                if (IsPostDom && IsExitBlock(block)) {
                    //blocks aren't connected to our fake exit block
                    //post_idom(n) of a exit block is itself
                    Assert(block.Succs.Count == 0);
                    newDom = entry;
                } else {
                    var predBlocks = IsPostDom ? block.Succs : block.Preds;
                    foreach (var predBlock in predBlocks) {
                        var pred = GetNode(predBlock);

                        if (pred.IDom != null) {
                            newDom = newDom == null ? pred : Intersect(pred, newDom);
                        }
                    }
                }

                if (newDom != node.IDom) {
                    node.IDom = newDom!;
                    changed = true;
                }
            }
        }

        static Node Intersect(Node b1, Node b2)
        {
            while (b1 != b2) {
                while (b1.PostIndex < b2.PostIndex) {
                    b1 = b1.IDom;
                }
                while (b2.PostIndex < b1.PostIndex) {
                    b2 = b2.IDom;
                }
            }
            return b1;
        }
    }

    private void ComputeChildren(List<Node> nodes)
    {
        var entryNode = nodes[^1];
        //Ignore entry node to avoid cycles in the children list
        for (int i = 0; i < nodes.Count - 1; i++) {
            var node = nodes[i];

            if (IsPostDom && node.IDom == entryNode) {
                //Change idom of exit nodes to itself
                node.IDom = node;
                continue;
            }
            var children = node.IDom.Children ??= new();
            children.Add(node);
        }
    }

    private bool IsExitBlock(BasicBlock block)
    {
        return block.Last is ReturnInst;
    }

    private class Node
    {
        public Node IDom = null!;
        public List<Node> Children = null!;
        public BasicBlock Block = null!;
        public int PostIndex;

        public override string ToString() => $"{Block} <- {IDom?.Block.ToString() ?? "?"}";
    }
}

public class DominanceFrontier
{
    private static HashSet<BasicBlock> _emptySet = new();
    private Dictionary<BasicBlock, HashSet<BasicBlock>> _df = new();

    public DominanceFrontier(DominatorTree domTree)
    {
        foreach (var block in domTree.Method) {
            if (block.Preds.Count < 2) continue;

            var blockDom = domTree.IDom(block);

            foreach (var pred in block.Preds) {
                var runner = pred;
                while (runner != blockDom) {
                    var frontier = _df.GetOrAddRef(runner) ??= new();
                    frontier.Add(block);

                    runner = domTree.IDom(runner);
                }
            }
        }
    }

    public IReadOnlySet<BasicBlock> Of(BasicBlock block)
        => _df.GetValueOrDefault(block, _emptySet);
}