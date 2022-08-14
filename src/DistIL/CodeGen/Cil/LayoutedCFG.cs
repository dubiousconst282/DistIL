namespace DistIL.CodeGen.Cil;

using DistIL.IR;

/// <summary>
/// Represents a flat list of basic blocks, ordered in such a way that maximizes the
/// number of fallthrough blocks, and groups together blocks in the same (protected) regions.
/// </summary>
public class LayoutedCFG
{
    public BasicBlock[] Blocks = null!;
    public LayoutedRegion[] Regions = null!;

    /// <summary> Maximize fallthrough blocks and group blocks in the same regions. </summary>
    public static LayoutedCFG Compute(MethodBody method)
    {
        var blocks = new BasicBlock[method.NumBlocks];
        var regions = Array.Empty<LayoutedRegion>();
        var visited = new ValueSet<BasicBlock>();
        int blockIdx = 0, regionIdx = 0;

        Recurse(method.EntryBlock, null!);
        Assert(blockIdx == blocks.Length);

        Array.Resize(ref regions, regionIdx);

        return new LayoutedCFG() {
            Blocks = blocks,
            Regions = regions
        };

        (int Start, int End) Recurse(BasicBlock entry, ArrayStack<BasicBlock> parentWorklist)
        {
            var worklist = new ArrayStack<BasicBlock>();
            worklist.Push(entry);
            visited.Add(entry);

            int startIdx = blockIdx;

            while (worklist.TryPop(out var block)) {
                //Recurse into new regions
                if (block != entry && block.Guards().Any()) {
                    //Mark handler blocks as visited to prevent them from being visited
                    foreach (var guard in block.Guards()) {
                        visited.Add(guard.HandlerBlock);
                        if (guard.HasFilter) {
                            visited.Add(guard.FilterBlock);
                        }
                    }
                    var tryRange = Recurse(block, worklist);

                    foreach (var guard in block.Guards()) {
                        Array.Resize(ref regions, (regionIdx + 4) & ~3); //grow in steps of 4
                        ref var region = ref regions[regionIdx++];
                        
                        region = new LayoutedRegion() {
                            Guard = guard, TryRange = tryRange
                        };
                        //Filter region must be before the handler region (see ExceptionRegion struct)
                        if (guard.HasFilter) {
                            visited.Remove(guard.FilterBlock);
                            region.FilterRange = Recurse(guard.FilterBlock, worklist);
                        }
                        visited.Remove(guard.HandlerBlock);
                        region.HandlerRange = Recurse(guard.HandlerBlock, worklist);
                    }
                    continue;
                }
                blocks[blockIdx++] = block;

                //Add succs into worklist (parent's if the block is leaving the current region)
                var destList = block.Last is LeaveInst or ContinueInst ? parentWorklist : worklist;
                foreach (var succ in block.Succs) {
                    if (visited.Add(succ)) {
                        destList.Push(succ);
                    }
                }
            }
            return (startIdx, blockIdx);
        }
    }

    /// <summary> Returns the `GuardInst` whose catch/filter handler entry block is `block` </summary>
    public GuardInst? GetCatchGuard(BasicBlock block)
    {
        foreach (ref var region in Regions.AsSpan()) {
            var guard = region.Guard;
            if (guard.Kind == GuardKind.Catch && (guard.HandlerBlock == block || guard.FilterBlock == block)) {
                return guard;
            }
        }
        return null;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendJoin(" ", Blocks.AsEnumerable());
        foreach (ref var region in Regions.AsSpan()) {
            string PrintRange((int s, int e) r) => $"{Blocks[r.s]}..{Blocks[r.e - 1]}";

            sb.Append($"\nTry={PrintRange(region.TryRange)} Handler={PrintRange(region.HandlerRange)} for `{region.Guard}`");
        }
        return sb.ToString();
    }
}
public struct LayoutedRegion
{
    public GuardInst Guard;
    public (int Start, int End) TryRange, HandlerRange, FilterRange;
}