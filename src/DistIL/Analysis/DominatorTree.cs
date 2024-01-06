namespace DistIL.Analysis;

public class DominatorTree : IMethodAnalysis
{
    readonly Dictionary<BasicBlock, Node> _block2node = new();
    readonly Node _root;
    bool _hasDfsIndices = false; // whether Node.{PreIndex, PostIndex} have been calculated

    public DominatorTree(MethodBody method)
    {
        var nodes = CreateNodes(method);
        _root = nodes[0];
        ComputeDom(nodes);
        ComputeChildren(nodes);
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new DominatorTree(mgr.Method);

    /// <summary> Returns the immediate dominator of <paramref name="block"/>, or itself if it's the entry block. </summary>
    public BasicBlock IDom(BasicBlock block)
    {
        return GetNode(block).IDom.Block;
    }

    /// <summary> Checks if all paths from the entry block must go through <paramref name="parent"/> before entering <paramref name="child"/>. </summary>
    public bool Dominates(BasicBlock parent, BasicBlock child)
    {
        if (parent == child) {
            return true;
        }

        var parentNode = GetNode(parent);
        var childNode = GetNode(child);

        if (childNode.IDom == parentNode) {
            return true;
        }

        if (!_hasDfsIndices) {
            ComputeDfsIndices();
        }
        return childNode.PreIndex >= parentNode.PreIndex &&
               childNode.PostIndex <= parentNode.PostIndex;
    }

    /// <summary> Checks if <paramref name="prev"/> is available when <paramref name="next"/> executes. </summary>
    public bool Dominates(Instruction prev, Instruction next)
    {
        if (prev.Block != next.Block) {
            return Dominates(prev.Block, next.Block);
        }
        // TODO: investigate if it's worth keeping instruction indices for fast intra-block dominance checks
        for (var inst = next; inst != null; inst = inst.Prev) {
            if (inst == prev) return true;
        }
        return false;
    }

    /// <summary> Same as <see cref="Dominates(BasicBlock, BasicBlock)"/>, but returns false if <paramref name="parent"/> and <paramref name="child"/> are the same block. </summary>
    public bool StrictlyDominates(BasicBlock parent, BasicBlock child)
    {
        return parent != child && Dominates(parent, child);
    }

    /// <summary> Performs a depth first traversal over this dominator tree. </summary>
    public void Traverse(Action<BasicBlock>? preVisit = null, Action<BasicBlock>? postVisit = null)
    {
        TraverseNodes(
            preVisit: node => preVisit?.Invoke(node.Block),
            postVisit: node => postVisit?.Invoke(node.Block)
        );
    }

    /// <summary> Enumerates all blocks immediately dominated by <paramref name="block"/>. </summary>
    public IEnumerable<BasicBlock> GetChildren(BasicBlock block)
    {
        var node = GetNode(block).FirstChild;
        for (; node != null; node = node.NextChild) {
            yield return node.Block;
        }
    }

    // NOTE: Querying nodes from unreachable blocks lead to KeyNotFoundException.
    //       Unsure how to best handle them, but ideally passes would not even consider unreachable blocks in the first place.
    private Node GetNode(BasicBlock block)
    {
        return _block2node[block];
    }

    /// <summary> Creates the tree nodes and returns an array with them in DFS post order. </summary>
    private Span<Node> CreateNodes(MethodBody method)
    {
        Debug.Assert(method.EntryBlock.NumPreds == 0);

        var nodes = new Node[method.NumBlocks];
        int index = nodes.Length;

        method.TraverseDepthFirst(postVisit: block => {
            var node = new Node() {
                Block = block,
                PostIndex = nodes.Length - index
            };
            _block2node.Add(block, node);
            nodes[--index] = node;
        });
        // index will only be >0 if there are unreachable blocks.
        return nodes.AsSpan(index);
    }

    // Algorithm from the paper "A Simple, Fast Dominance Algorithm"
    // https://www.cs.rice.edu/~keith/EMBED/dom.pdf
    private void ComputeDom(Span<Node> nodesRPO)
    {
        var entry = nodesRPO[0];
        entry.IDom = entry; // entry block dominates itself

        bool changed = true;
        while (changed) {
            changed = false;
            // foreach block in reverse post order, except entry (at index 0)
            foreach (var node in nodesRPO[1..]) {
                var newDom = default(Node);

                foreach (var predBlock in node.Block.Preds) {
                    var pred = _block2node.GetValueOrDefault(predBlock);

                    if (pred?.IDom != null) {
                        newDom = newDom == null ? pred : Intersect(pred, newDom);
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

    private static void ComputeChildren(Span<Node> nodes)
    {
        // Ignore entry node (index 0) to avoid cycles in the children list
        foreach (var node in nodes[1..]) {
            var parent = node.IDom;
            if (parent.FirstChild == null) {
                parent.FirstChild = node;
            } else {
                Debug.Assert(node.NextChild == null);
                node.NextChild = parent.FirstChild.NextChild;
                parent.FirstChild.NextChild = node;
            }
        }
    }

    private void ComputeDfsIndices()
    {
        int index = 0;
        TraverseNodes(
            preVisit: node => node.PreIndex = ++index,
            postVisit: node => node.PostIndex = index
        );
        _hasDfsIndices = true;
    }

    private void TraverseNodes(Action<Node>? preVisit, Action<Node>? postVisit)
    {
        preVisit?.Invoke(_root);

        var worklist = new ArrayStack<(Node Node, Node? NextChild)>();
        worklist.Push((_root, _root.FirstChild));

        while (!worklist.IsEmpty) {
            ref var curr = ref worklist.Top;
            var child = curr.NextChild;

            if (child != null) {
                curr.NextChild = child.NextChild;
                worklist.Push((child, child.FirstChild));
                preVisit?.Invoke(child);
            } else {
                postVisit?.Invoke(curr.Node);
                worklist.Pop();
            }
        }
    }

    class Node
    {
        public Node IDom = null!;
        public Node? FirstChild, NextChild; // Links for the children list
        public BasicBlock Block = null!;
        // ComputeDom() assumes that PostIndex holds the post DFS index of each block.
        // Once dominance is computed and when _hasDfsIndices is true, these contain
        // the DFS indices of the actual dominance tree, used for O(1) dominance checks.
        public int PreIndex, PostIndex;

        public override string ToString() => $"{Block} <- {IDom?.Block.ToString() ?? "?"}";
    }
}

public class DominanceFrontier : IMethodAnalysis
{
    static readonly RefSet<BasicBlock> _emptySet = new();
    readonly Dictionary<BasicBlock, RefSet<BasicBlock>> _df = new();

    public DominanceFrontier(MethodBody method, DominatorTree domTree)
    {
        foreach (var block in method) {
            if (block.NumPreds < 2) continue;

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

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new DominanceFrontier(mgr.Method, mgr.GetAnalysis<DominatorTree>());

    public RefSet<BasicBlock> Of(BasicBlock block)
        => _df.GetValueOrDefault(block, _emptySet);
}