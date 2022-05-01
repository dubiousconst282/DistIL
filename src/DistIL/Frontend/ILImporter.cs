namespace DistIL.Frontend;

using DistIL.AsmIO;
using ExceptionRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

public class ILImporter
{
    public MethodDef Method { get; }
    readonly Dictionary<int, BlockState> _blocks = new();

    public ILImporter(MethodDef method)
    {
        Method = method;
        if (method.Body == null) {
            throw new ArgumentException("Method has no body to import");
        }
    }

    public void ImportCode()
    {
        var body = Method.Body!;
        //Find leaders (block boundaries)
        var code = body.Instructions.AsSpan();
        var leaders = FindLeaders(code, body.ExceptionRegions);

        //Create blocks
        int startOffset = 0;
        foreach (int endOffset in leaders) {
            _blocks[startOffset] = new BlockState(this, startOffset);
            startOffset = endOffset;
        }
        //Ensure that the entry block don't have predecessors
        if (leaders.Remove(0)) {
            var entryBlock = Method.CreateBlock();
            var firstBlock = GetBlock(0).Block;
            entryBlock.SetBranch(firstBlock);
        }
        //Import blocks
        int startIndex = 0;
        foreach (int endOffset in leaders) {
            var block = GetBlock(code[startIndex].Offset);
            int endIndex = FindIndex(code, endOffset);
            block.ImportCode(code[startIndex..endIndex]);
            startIndex = endIndex;
        }
    }

    //Returns a bitset containing instruction offsets marking block starts (branch targets).
    private static BitSet FindLeaders(Span<ILInstruction> code, List<ExceptionRegion> ehRegions)
    {
        int codeSize = code[^1].GetEndOffset();
        var leaders = new BitSet(codeSize);

        foreach (ref var inst in code) {
            if (!BlockState.IsTerminator(ref inst)) continue;

            if (inst.Operand is int targetOffset) {
                leaders.Set(targetOffset);
            }
            //switch
            else if (inst.Operand is int[] targetOffsets) {
                foreach (int offset in targetOffsets) {
                    leaders.Set(offset);
                }
            }
            leaders.Set(inst.GetEndOffset()); //fallthrough
        }

        foreach (var region in ehRegions) {
            leaders.Set(region.TryOffset);
            leaders.Set(region.TryOffset + region.TryLength);

            if (region.Kind == ExceptionRegionKind.Catch) {
                leaders.Set(region.HandlerOffset);
                leaders.Set(region.HandlerOffset + region.HandlerLength);
            } else {
                throw new NotImplementedException();
            }
        }
        return leaders;
    }
    //Binary search to find instruction index using offset
    private static int FindIndex(Span<ILInstruction> code, int offset)
    {
        int start = 0;
        int end = code.Length - 1;
        while (start <= end) {
            int mid = (start + end) / 2;
            int c = offset - code[mid].Offset;
            if (c < 0) {
                end = mid - 1;
            } else if (c > 0) {
                start = mid + 1;
            } else {
                return mid;
            }
        }
        //Special case last instruction
        if (offset >= code[^1].Offset) {
            return code.Length;
        }
        throw new InvalidProgramException("Invalid instruction offset");
    }

    /// <summary> Gets or creates a block for the specified instruction offset. </summary>
    internal BlockState GetBlock(int offset) => _blocks[offset];
}
