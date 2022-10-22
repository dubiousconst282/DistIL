namespace DistIL.CodeGen.Cil;

using DistIL.Analysis;

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
        int blockIdx = 0, numGuards = 0;

        foreach (var block in method) {
            blocks[blockIdx++] = block;
            numGuards += block.Guards().Count();
        }

        if (numGuards > 0) {
            var regionAnalysis = new ProtectedRegionAnalysis(method);
            var clauseIndices = new Dictionary<GuardInst, int>(numGuards);
            var blockIndices = new Dictionary<BasicBlock, int>(method.NumBlocks);
            var orderedBlocks = blocks.AsSpan().ToArray(); //copy
            regions = new LayoutedRegion[numGuards];
            blockIdx = 0;

            for (int i = 0; i < blocks.Length; i++) {
                blockIndices[blocks[i]] = i;
            }
            LayoutRegion(regionAnalysis.Root);
            
            (int Start, int End) LayoutRegion(ProtectedRegion node)
            {
                int startIdx = blockIdx;

                var remainingBlocks = new BitSet();
                int firstBlockIdx = 0;
                foreach (var block in node.Blocks) {
                    remainingBlocks.Add(blockIndices[block]);
                }

                //Recurse into children regions
                foreach (var child in node.Children) {
                    PlaceAntecessorBlocks(child.StartBlock);
                    var range = LayoutRegion(child);

                    var guard = (GuardInst?)child.StartBlock.Users().FirstOrDefault(u => u is GuardInst);
                    if (guard != null) {
                        GetRegion(guard).UpdateRanges(child.StartBlock, range);
                    }
                }
                PlaceAntecessorBlocks(null);

                foreach (var guard in node.StartBlock.Guards()) {
                    GetRegion(guard).UpdateRanges(node.StartBlock, (startIdx, blockIdx));
                }
                return (startIdx, blockIdx);

                void PlaceAntecessorBlocks(BasicBlock? limit)
                {
                    int endIdx = limit != null ? blockIndices[limit] : blockIndices.Count;
                    foreach (int idx in remainingBlocks.GetRangeEnumerator(firstBlockIdx, endIdx)) {
                        blocks[blockIdx++] = orderedBlocks[idx];
                    }
                    firstBlockIdx = endIdx;
                }
            }
            ref LayoutedRegion GetRegion(GuardInst guard)
            {
                ref int regionIdx = ref clauseIndices.GetOrAddRef(guard, out bool regionIdxExists);
                if (!regionIdxExists) {
                    regionIdx = clauseIndices.Count - 1;
                    regions[regionIdx].Guard = guard;
                }
                return ref regions[regionIdx];
            }
        }

        return new LayoutedCFG() {
            Blocks = blocks,
            Regions = regions
        };
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

    internal void UpdateRanges(BasicBlock entryBlock, (int start, int end) range)
    {
        if (entryBlock == Guard.Block) {
            TryRange = range;
        } else if (entryBlock == Guard.HandlerBlock) {
            HandlerRange = range;
        } else if (entryBlock == Guard.FilterBlock) {
            FilterRange = range;
        } else {
            throw new UnreachableException();
        }
    }
}