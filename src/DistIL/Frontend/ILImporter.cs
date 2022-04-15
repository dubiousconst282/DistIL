namespace DistIL.Frontend;

using DistIL.IR;
using DistIL.AsmIO;

public class ILImporter
{
    public MethodDef Method { get; }
    readonly Variable[] _vars;
    readonly Dictionary<int, BlockState> _blocks = new();

    public ILImporter(MethodDef method)
    {
        Method = method;
        _vars = GetLocalVars();
    }

    public void ImportCode()
    {
        //Decode instructions and find block boundaries (leaders)
        var reader = Method.Body!.GetILReader();
        var code = new List<ILInstruction>(reader.Length / 2);
        while (reader.Offset < reader.Length) {
            var inst = ILInstruction.Decode(ref reader);
            code.Add(inst);
        }
        var leaders = FindLeaders(code.AsSpan());

        //Ensure that the entry block don't have predecessors
        if (leaders.Remove(0)) {
            var entryBlock = Method.CreateBlock();
            var firstBlock = GetBlock(0).Block;
            entryBlock.SetBranch(firstBlock);
        }
        //Build CFG and import code
        int startIndex = 0;
        foreach (int endIndex in leaders) {
            int offset = code[startIndex].Offset;
            var block = GetBlock(offset);
            block.ImportCode(code.AsSpan(), startIndex, endIndex);

            startIndex = endIndex;
        }
    }

    //Returns a bitset containing all block starts (branch targets).
    private BitSet FindLeaders(Span<ILInstruction> code)
    {
        var leaders = new BitSet(code.Length + 1);
        leaders.Set(code.Length); //add a leader after the last inst to make the import loop simpler

        for (int i = 0; i < code.Length; i++) {
            ref var inst = ref code[i];
            if (!BlockState.IsTerminator(ref inst)) continue;

            if (inst.Operand is int targetOffset) {
                leaders.Set(FindIndex(code, targetOffset));
            }
            //switch
            else if (inst.Operand is int[] targetOffsets) {
                foreach (int offset in targetOffsets) {
                    leaders.Set(FindIndex(code, offset));
                }
            }
            leaders.Set(i + 1); //fallthrough
        }
        return leaders;
    }
    //Binary search to find instruction index using offset
    private int FindIndex(Span<ILInstruction> code, int offset)
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
        throw new InvalidProgramException("Invalid instruction offset");
    }

    private Variable[] GetLocalVars()
    {
        var body = Method.Body!;
        var mod = Method.Module;

        if (body.LocalSignature.IsNil) {
            return Array.Empty<Variable>();
        }
        var sig = mod.Reader.GetStandaloneSignature(body.LocalSignature);
        var types = sig.DecodeLocalSignature(mod.TypeDecoder, null);
        var vars = new Variable[types.Length];
        for (int i = 0; i < vars.Length; i++) {
            var type = types[i];
            bool isPinned = false;
            if (type is PinnedType_ pinnedType) {
                type = pinnedType.ElemType;
                isPinned = true;
            }
            vars[i] = new Variable(type, isPinned, "loc" + (i + 1));
        }
        return vars;
    }

    /// <summary> Gets or creates a block for the specified instruction offset. </summary>
    internal BlockState GetBlock(int offset)
    {
        ref var state = ref _blocks.GetOrAddRef(offset);
        state ??= new BlockState(this, offset);
        return state;
    }

    internal Variable GetVar(int index, bool isArg) 
        => isArg ? Method.Args[index] : _vars[index];
}
