namespace DistIL.Frontend;

using DistIL.AsmIO;
using DistIL.IR;

using ExceptionRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

public class ILImporter
{
    public MethodDef Method { get; }

    internal MethodBody _body;
    internal VarFlags[] _varFlags; //Used to discover variables crossing try blocks/exposed address
    internal Variable[] _argSlots; //Argument variables

    readonly Dictionary<int, BlockState> _blocks = new();

    public ILImporter(MethodDef method)
    {
        Method = method;
        if (method.ILBody == null) {
            throw new ArgumentException("Method has no body to import");
        }
        int numVars = method.Params.Length + method.ILBody.Locals.Count;
        _argSlots = new Variable[method.Params.Length];
        _varFlags = new VarFlags[numVars];
        _body = new MethodBody(method);
    }

    public MethodBody ImportCode()
    {
        var body = Method.ILBody!;
        var code = body.Instructions.AsSpan();
        var ehRegions = body.ExceptionRegions;
        var leaders = FindLeaders(code, ehRegions);

        CreateBlocks(leaders);
        GuardRegions(leaders, ehRegions);
        ImportBlocks(code, leaders);
        return _body;
    }

    private void CreateBlocks(BitSet leaders)
    {
        //Remove 0th label to avoid creating 2 blocks
        bool firstHasPred = leaders.Remove(0);
        var entryBlock = firstHasPred ? _body.CreateBlock() : null!;

        int startOffset = 0;
        foreach (int endOffset in leaders) {
            _blocks[startOffset] = new BlockState(this);
            startOffset = endOffset;
        }
        //Ensure that the entry block don't have predecessors
        if (firstHasPred) {
            var firstBlock = GetBlock(0).Block;
            entryBlock.SetBranch(firstBlock);
        }
    }

    private void GuardRegions(BitSet leaders, List<ExceptionRegion> ehRegions)
    {
        //I.12.4.2.5 Overview of exception handling
        foreach (var region in ehRegions) {
            var kind = region.Kind switch {
                ExceptionRegionKind.Catch or
                ExceptionRegionKind.Filter  => GuardKind.Catch,
                ExceptionRegionKind.Finally => GuardKind.Finally,
                ExceptionRegionKind.Fault   => GuardKind.Fault,
                _ => throw new InvalidOperationException()
            };
            bool hasFilter = region.Kind == ExceptionRegionKind.Filter;

            var startBlock = GetBlock(region.TryStart);
            var handlerBlock = GetBlock(region.HandlerStart);
            var filterBlock = hasFilter ? GetBlock(region.FilterStart) : null;
            var guard = new GuardInst(kind, handlerBlock.Block, region.CatchType, filterBlock?.Block);
            startBlock.Emit(guard);
            startBlock.Block.Connect(handlerBlock.Block); //dummy edge to avoid unreachable blocks

            //Push exception on handler/filter entry stack
            if (kind == GuardKind.Catch) {
                handlerBlock.PushNoEmit(guard);
            }
            if (hasFilter) {
                filterBlock!.PushNoEmit(guard);
                startBlock.Block.Connect(filterBlock.Block);
                ActiveRegion(guard, region.FilterStart, region.FilterEnd);
            }
            //Set active region for leave/endf* instructions
            ActiveRegion(guard, region.TryStart, region.TryEnd);
            ActiveRegion(guard, region.HandlerStart, region.HandlerEnd);
        }

        void ActiveRegion(GuardInst guard, int start, int end)
        {
            //leaders[0] is always unset at this point
            if (start == 0) {
                GetBlock(0).SetActiveGuard(guard);
            }
            foreach (int offset in leaders.GetRangeEnumerator(start, end)) {
                GetBlock(offset).SetActiveGuard(guard);
            }
        }
    }

    private void ImportBlocks(Span<ILInstruction> code, BitSet leaders)
    {
        //Insert argument copies to local vars on the entry block
        var entryBlock = _body.EntryBlock ?? GetBlock(0).Block;
        var firstInst = entryBlock.First?.Prev;
        var args = _body.Args;
        for (int i = 0; i < args.Length; i++) {
            var arg = args[i];
            var slot = _argSlots[i] = new Variable(arg.ResultType, name: $"a_{arg.Name}");
            var store = new StoreVarInst(slot, arg);
            entryBlock.InsertAfter(firstInst, store);
            firstInst = store;
        }

        //Import code
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
            if (!inst.OpCode.IsTerminator()) continue;

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
            //Note: end offsets must have already been marked by leave/endfinally
            leaders.Set(region.TryStart);

            if (region.HandlerStart >= 0) {
                leaders.Set(region.HandlerStart);
            }
            if (region.FilterStart >= 0) {
                leaders.Set(region.FilterStart);
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
