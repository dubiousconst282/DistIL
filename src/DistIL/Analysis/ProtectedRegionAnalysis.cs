namespace DistIL.Analysis;

public class ProtectedRegionAnalysis : IMethodAnalysis
{
    public ProtectedRegion Root { get; }

    public ProtectedRegionAnalysis(MethodBody method)
    {
        Root = new ProtectedRegion() { StartBlock = method.EntryBlock };

        var visited = new RefSet<BasicBlock>();
        var worklist = new ArrayStack<(BasicBlock Block, ProtectedRegion Region)>();

        worklist.Push((method.EntryBlock, Root));
        visited.Add(method.EntryBlock);

        while (worklist.TryPop(out var node)) {
            var (block, region) = node;

            // Push sub regions
            // Note that handler/filters can't have guards, the second condition is fine.
            if (block.Guards().Any() && block != region.StartBlock) {
                visited.Remove(block);
                PushChild(block);

                foreach (var guard in block.Guards()) {
                    if (guard.HasFilter) {
                        PushChild(guard.FilterBlock);
                    }
                    PushChild(guard.HandlerBlock);
                }
                continue;
            }
            region.Blocks.Add(block);

            // Don't need to visit resume edges because they only exist only for consistency.
            if (block.Last is ResumeInst) continue;

            // Add succs to worklist
            var succRegion = block.Last is LeaveInst ? region.Parent! : region;
            foreach (var succ in block.Succs) {
                if (visited.Add(succ)) {
                    worklist.Push((succ, succRegion));
                }
            }

            void PushChild(BasicBlock entry)
            {
                if (!visited.Add(entry)) return;

                var child = new ProtectedRegion() {
                    StartBlock = entry,
                    Parent = region
                };
                region.Children.Add(child);
                worklist.Push((entry, child));
            }
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new ProtectedRegionAnalysis(mgr.Method);

    public ProtectedRegion GetBlockRegion(BasicBlock block)
        => Root.FindInnermostParent(block)
            ?? throw new InvalidOperationException("Block is not a child of any region (is it a new block?)");
}
public class ProtectedRegion
{
    public RefSet<BasicBlock> Blocks { get; } = new();
    /// <remarks> Guaranteed to be ordered as: [protectedRegion, filterRegion?, handlerRegion] pairs/triples for each guard in <see cref="StartBlock"/>. </remarks>
    public List<ProtectedRegion> Children { get; } = new();
    public ProtectedRegion? Parent { get; set; }

    public BasicBlock StartBlock { get; set; } = null!;

    public IEnumerable<BasicBlock> GetExitBlocks()
    {
        foreach (var block in Blocks) {
            if (block.Last is LeaveInst or ResumeInst) {
                yield return block;
            }
        }
    }

    public ProtectedRegion? FindInnermostParent(BasicBlock block)
    {
        if (Blocks.Contains(block)) {
            return this;
        }
        foreach (var child in Children) {
            if (child.FindInnermostParent(block) is { } region) {
                return region;
            }
        }
        return null;
    }

    /// <summary> If this is a handler region, returns the guard defining it. </summary>
    public GuardInst? GetHandlerGuard() => StartBlock.Users().OfType<GuardInst>().FirstOrDefault();
}