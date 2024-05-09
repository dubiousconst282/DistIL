namespace DistIL.CodeGen.Cil;

using DistIL.Analysis;

/// <summary>
/// Represents a flat list of basic blocks, ordered in such a way that protected regions
/// are grouped together sequentially, for CIL compatibility.
/// </summary>
public class LayoutedCFG
{
    public BasicBlock[] Blocks = null!;
    public LayoutedRegion[] Regions = null!;

    public static LayoutedCFG Compute(MethodBody method)
    {
        var blocks = new BasicBlock[method.NumBlocks];
        var regions = Array.Empty<LayoutedRegion>();
        int blockIdx = 0, numGuards = 0;

        foreach (var block in method) {
            blocks[blockIdx++] = block;
            numGuards += block.Guards().Count();
        }

        if (numGuards > 0) {
            var orderedBlocks = blocks;
            blocks = new BasicBlock[method.NumBlocks];
            regions = new LayoutedRegion[numGuards];
            LayoutWithRegions(method, orderedBlocks, blocks, regions);
        }
        return new LayoutedCFG() {
            Blocks = blocks,
            Regions = regions
        };
    }

    private static void LayoutWithRegions(MethodBody method, BasicBlock[] orderedBlocks, BasicBlock[] laidBlocks, LayoutedRegion[] clauses)
    {
        var clauseIndices = new Dictionary<GuardInst, int>(clauses.Length);
        var blockIndices = new Dictionary<BasicBlock, int>(method.NumBlocks);
        int blockIdx = 0;

        for (int i = 0; i < orderedBlocks.Length; i++) {
            blockIndices[orderedBlocks[i]] = i;
        }
        var regionAnalysis = new ProtectedRegionAnalysis(method);
        LayoutRegion(regionAnalysis.Root);

        AbsRange LayoutRegion(ProtectedRegion node)
        {
            int startIdx = blockIdx;
            var regionBlockIndices = new BitSet();

            // Sort region blocks to their original order
            // Bit sets are naturally ordered and allows this to be done efficiently.
            foreach (var block in node.Blocks) {
                regionBlockIndices.Add(blockIndices[block]);
            }

            // Start block must always come first
            laidBlocks[blockIdx++] = node.StartBlock;
            regionBlockIndices.Remove(blockIndices[node.StartBlock]);

            // Recurse into child regions and place blocks appearing before them
            foreach (var child in node.Children) {
                PlaceAntecessorBlocks(child.StartBlock);
                var range = LayoutRegion(child);
                var guard = child.GetHandlerGuard();

                if (guard != null) {
                    clauses[clauseIndices[guard]].UpdateRanges(child.StartBlock, range);
                }
            }
            PlaceAntecessorBlocks(null);

            // Create region clauses
            foreach (var guard in node.StartBlock.Guards()) {
                int clauseIdx = clauseIndices.Count;
                clauseIndices.Add(guard, clauseIdx);

                clauses[clauseIdx] = new() {
                    Guard = guard,
                    TryRange = (startIdx, blockIdx)
                };
            }
            return (startIdx, blockIdx);

            void PlaceAntecessorBlocks(BasicBlock? limit)
            {
                int endIdx = limit != null ? blockIndices[limit] : blockIndices.Count;
                foreach (int idx in regionBlockIndices.GetRangeEnumerator(0, endIdx)) {
                    laidBlocks[blockIdx++] = orderedBlocks[idx];
                    regionBlockIndices.Remove(idx);
                }
            }
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendJoin(" ", Blocks.AsEnumerable());
        foreach (ref var region in Regions.AsSpan()) {
            string PrintRange(AbsRange r) => $"{Blocks[r.Start]}..{Blocks[r.End - 1]}";

            sb.Append($"\nTry={PrintRange(region.TryRange)} Handler={PrintRange(region.HandlerRange)} for `{region.Guard}`");
        }
        return sb.ToString();
    }
}
public struct LayoutedRegion
{
    public GuardInst Guard;
    public AbsRange TryRange, HandlerRange, FilterRange;

    internal void UpdateRanges(BasicBlock entryBlock, AbsRange range)
    {
        if (entryBlock == Guard.HandlerBlock) {
            HandlerRange = range;
        } else if (entryBlock == Guard.FilterBlock) {
            FilterRange = range;
        } else {
            throw new UnreachableException();
        }
    }
}