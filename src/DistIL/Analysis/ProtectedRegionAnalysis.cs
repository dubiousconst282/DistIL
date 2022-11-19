namespace DistIL.Analysis;

public class ProtectedRegionAnalysis : IMethodAnalysis
{
    public ProtectedRegion Root { get; }

    public ProtectedRegionAnalysis(MethodBody method)
    {
        var visited = new RefSet<BasicBlock>();

        Root = Recurse(method.EntryBlock, null!);
        
        ProtectedRegion Recurse(BasicBlock entry, ArrayStack<BasicBlock> parentWorklist)
        {
            var region = new ProtectedRegion() { StartBlock = entry };
            var worklist = new ArrayStack<BasicBlock>();
            worklist.Push(entry);
            visited.Add(entry);

            while (worklist.TryPop(out var block)) {
                //Recurse into new regions
                if (block != entry && block.Guards().Any()) {
                    int childIdx = region.AllocChild();

                    foreach (var guard in block.Guards()) {
                        if (guard.HasFilter) {
                            region.AddChild(Recurse(guard.FilterBlock, worklist));
                        }
                        region.AddChild(Recurse(guard.HandlerBlock, worklist));
                    }
                    region.AddChild(Recurse(block, worklist), childIdx);
                    continue;
                }
                region.Blocks.Add(block);

                //Add succs into worklist (parent's if the block is leaving the current region)
                var destList = block.Last is LeaveInst ? parentWorklist : worklist;
                foreach (var succ in block.Succs) {
                    if (visited.Add(succ)) {
                        destList.Push(succ);
                    }
                }
            }
            return region;
        }
    }

    public static IMethodAnalysis Create(IMethodAnalysisManager mgr)
    {
        return new ProtectedRegionAnalysis(mgr.Method);
    }

    public ProtectedRegion GetBlockRegion(BasicBlock block)
        => Root.FindBlockParent(block)
            ?? throw new InvalidOperationException("Block is not a child of any region (is it a new block?)");
}
public class ProtectedRegion
{
    public RefSet<BasicBlock> Blocks { get; } = new();
    /// <remarks> Guaranteed to be ordered as: [protectedRegion, filterRegion?, handlerRegion] pairs/triples for each guard in `StartBlock`. </remarks>
    public List<ProtectedRegion> Children { get; } = new();
    public ProtectedRegion? Parent { get; private set; }

    public BasicBlock StartBlock { get; init; } = null!;

    public IEnumerable<BasicBlock> GetExitBlocks()
    {
        foreach (var block in Blocks) {
            if (block.Last is LeaveInst or ResumeInst) {
                yield return block;
            }
        }
    }

    public ProtectedRegion? FindBlockParent(BasicBlock block)
    {
        if (Blocks.Contains(block)) {
            return this;
        }
        foreach (var child in Children) {
            if (child.FindBlockParent(block) is { } region) {
                return region;
            }
        }
        return null;
    }

    internal void AddChild(ProtectedRegion child, int index = -1)
    {
        child.Parent = this;

        if (index < 0) {
            Children.Add(child);
        } else {
            Children[index] = child;
        }
    }
    internal int AllocChild()
    {
        Children.Add(null!);
        return Children.Count - 1;
    }
}