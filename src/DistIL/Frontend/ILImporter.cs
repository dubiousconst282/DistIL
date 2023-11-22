namespace DistIL.Frontend;

using ExceptionRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

public class ILImporter
{
    internal readonly MethodDef _method;
    internal readonly MethodBody _body;
    internal readonly RegionNode? _regionTree;

    internal readonly ILMethodBody _ilBody;
    internal readonly VarFlags[] _varFlags;
    internal readonly Value?[] _varSlots;

    readonly Dictionary<int, BlockState> _blocks = new();

    private ILImporter(MethodDef method)
    {
        Ensure.That(method.ILBody != null);

        _ilBody = method.ILBody;
        _method = method;
        _body = new MethodBody(method);

        _regionTree = RegionNode.BuildTree(_ilBody.ExceptionClauses);

        int numVars = _body.Args.Length + _ilBody.Locals.Length;
        _varFlags = new VarFlags[numVars];
        _varSlots = new Value[numVars];
    }

    public static MethodBody ParseCode(MethodDef method)
    {
        return new ILImporter(method).ImportCode();
    }

    private MethodBody ImportCode()
    {
        var code = _ilBody.Instructions.AsSpan();
        var ehRegions = _ilBody.ExceptionClauses;
        var leaders = FindLeaders(code, ehRegions);

        AnalyseVars(code, leaders);
        CreateBlocks(leaders);
        CreateGuards(ehRegions);
        ImportBlocks(code, leaders);
        return _body;
    }

    private void CreateBlocks(BitSet leaders)
    {
        // Remove 0th label to avoid creating 2 blocks
        bool firstHasPred = leaders.Remove(0);
        var entryBlock = firstHasPred ? _body.CreateBlock() : null!;

        int startOffset = 0;
        foreach (int endOffset in leaders) {
            _blocks[startOffset] = new BlockState(this, startOffset);
            startOffset = endOffset;
        }
        // Ensure that the entry block don't have predecessors
        if (firstHasPred) {
            var firstBlock = GetBlock(0).Block;
            entryBlock.SetBranch(firstBlock);
        }
    }

    private void CreateGuards(ExceptionClause[] clauses)
    {
        var mappings = new Dictionary<GuardInst, ExceptionClause>(clauses.Length);

        // I.12.4.2.5 Overview of exception handling
        foreach (var clause in clauses) {
            var kind = clause.Kind switch {
                ExceptionRegionKind.Catch or
                ExceptionRegionKind.Filter  => GuardKind.Catch,
                ExceptionRegionKind.Finally => GuardKind.Finally,
                ExceptionRegionKind.Fault   => GuardKind.Fault,
                _ => throw new InvalidOperationException()
            };
            bool hasFilter = clause.Kind == ExceptionRegionKind.Filter;

            var startBlock = GetOrSplitStartBlock(clause);
            var handlerBlock = GetBlock(clause.HandlerStart);
            var filterBlock = hasFilter ? GetBlock(clause.FilterStart) : null;

            var guard = new GuardInst(kind, handlerBlock.Block, clause.CatchType, filterBlock?.Block);
            startBlock.InsertAnteLast(guard);

            // Push exception on handler/filter entry stack
            if (kind == GuardKind.Catch) {
                handlerBlock.PushNoEmit(guard);
            }
            if (hasFilter) {
                filterBlock!.PushNoEmit(guard);
            }
            mappings[guard] = clause;
        }

        BasicBlock GetOrSplitStartBlock(ExceptionClause region)
        {
            var state = GetBlock(region.TryStart);

            // Create a new dominating block for this region if it nests any other in the current block.
            // Note that this code relies on the region table to be correctly ordered, as required by ECMA335:
            //  "If handlers are nested, the most deeply nested try blocks shall come
            //  before the try blocks that enclose them."
            // TODO: consider using the region tree for this
            // FIXME: insert dummy jump block for regions starting in the entry block of a handler/filter
            if (IsBlockNestedBy(region, state.EntryBlock)) {
                var newBlock = _body.CreateBlock(insertAfter: state.EntryBlock.Prev);

                foreach (var pred in state.EntryBlock.Preds) {
                    Debug.Assert(pred.NumSuccs == 1);
                    pred.SetBranch(newBlock);
                }
                newBlock.SetBranch(state.EntryBlock);
                state.EntryBlock = newBlock;
            }
            return state.EntryBlock;
        }
        bool IsBlockNestedBy(ExceptionClause region, BasicBlock block)
        {
            foreach (var guard in block.Guards()) {
                var currRegion = mappings[guard];
                if (currRegion.TryStart >= region.TryStart && currRegion.TryEnd < region.TryEnd) {
                    return true;
                }
            }
            return false;
        }
    }

    private void ImportBlocks(Span<ILInstruction> code, BitSet leaders)
    {
        var entryBlock = _body.EntryBlock ?? GetBlock(0).Block;

        // Copy stored/address taken arguments to local variables
        for (int i = 0; i < _body.Args.Length; i++) {
            if (!Has(_varFlags[i], VarFlags.AddrTaken | VarFlags.Stored)) continue;

            var arg = _body.Args[i];
            var slot = _varSlots[i] = new LocalSlot(arg.ResultType, $"a_{arg.Name}");
            entryBlock.InsertAnteLast(new StoreInst(slot, arg));
        }

        // Import code
        int startIndex = 0;
        foreach (int endOffset in leaders) {
            var block = GetBlock(code[startIndex].Offset);
            int endIndex = FindIndex(code, endOffset);
            block.ImportCode(code[startIndex..endIndex]);
            startIndex = endIndex;
        }
    }

    private void AnalyseVars(Span<ILInstruction> code, BitSet leaders)
    {
        int blockStartIdx = 0;
        var lastInfos = new (int BlockIdx, int StoreRegionOffset, int LoadOffset)[_ilBody.Locals.Length];

        foreach (int endOffset in leaders) {
            int blockEndIdx = FindIndex(code, endOffset);

            foreach (ref var inst in code[blockStartIdx..blockEndIdx]) {
                var (op, varIdx) = GetVarInstOp(inst.OpCode, inst.Operand);
                if (op == VarFlags.None) continue;

                if (Has(op, VarFlags.IsLocal)) {
                    CalcLocalVarFlags(ref inst, ref op, varIdx);
                    varIdx += _body.Args.Length;
                }
                _varFlags[varIdx] |= op;
            }
            blockStartIdx = blockEndIdx;
        }

        void CalcLocalVarFlags(ref ILInstruction inst, ref VarFlags flags, int varIdx)
        {
            ref var lastInfo = ref lastInfos[varIdx];

            if (lastInfo.BlockIdx != blockStartIdx + 1) {
                if (lastInfo.BlockIdx != 0) {
                    flags |= VarFlags.CrossesBlock;
                }
                lastInfo.BlockIdx = blockStartIdx + 1;
            }
            // When loading a var, we must mark it as exposed if there are stores in:
            // - a neighbor region -- filter{store x}; catch{load x}
            // - a child region -- guard{store x}; load x;
            // Only the following is allowed:
            // - a parent region -- store x; guard{load x}; store x;
            //
            // There's also no guarantee for block ordering, these conds must be checked on both ops.
            if (_regionTree != null && Has(flags, VarFlags.Stored | VarFlags.Loaded)) {
                if (Has(flags, VarFlags.Stored)) {
                    ref int lastRegion = ref lastInfo.StoreRegionOffset;
                    var currRegion = _regionTree.FindEnclosing(inst.Offset).StartOffset + 1;

                    if (lastRegion == 0) {
                        lastRegion = currRegion;
                    } else if (lastRegion != currRegion) {
                        lastRegion = -1; // multiple stores in different regions
                    }
                } else {
                    lastInfo.LoadOffset = inst.Offset + 1;
                }
                
                if (CrossesRegions(lastInfo.StoreRegionOffset, lastInfo.LoadOffset)) {
                    flags |= VarFlags.CrossesRegions;
                }
            }
        }
        bool CrossesRegions(int storeRegionOffset, int loadOffset)
        {
            if (storeRegionOffset < 0 || (storeRegionOffset == 0 && loadOffset != 0)) {
                return true; // multiple stores on different regions or load before store (be conservative for bad block order)
            }
            if (loadOffset != 0) {
                var storeRegion = _regionTree.FindEnclosing(storeRegionOffset - 1);
                return !storeRegion.Contains(loadOffset - 1);
            }
            return false;
        }
    }

    // Returns a bitset containing all instruction offsets where a block starts (branch targets).
    private static BitSet FindLeaders(Span<ILInstruction> code, ExceptionClause[] ehRegions)
    {
        int codeSize = code[^1].GetEndOffset();
        var leaders = new BitSet(codeSize);

        foreach (ref var inst in code) {
            if (!inst.OpCode.IsTerminator()) continue;

            if (inst.Operand is int targetOffset) {
                leaders.Add(targetOffset);
            }
            // switch
            else if (inst.Operand is int[] targetOffsets) {
                foreach (int offset in targetOffsets) {
                    leaders.Add(offset);
                }
            }
            leaders.Add(inst.GetEndOffset()); // fallthrough
        }

        foreach (var region in ehRegions) {
            // Note: end offsets must have already been marked by leave/endfinally
            leaders.Add(region.TryStart);

            if (region.HandlerStart >= 0) {
                leaders.Add(region.HandlerStart);
            }
            if (region.FilterStart >= 0) {
                leaders.Add(region.FilterStart);
            }
        }
        return leaders;
    }

    // Binary search to find instruction index using offset
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
        // Special case last instruction
        if (offset >= code[^1].Offset) {
            return code.Length;
        }
        throw new InvalidProgramException("Invalid instruction offset");
    }

    /// <summary> Gets the block for the leader instruction at the specified offset. </summary>
    internal BlockState GetBlock(int offset) => _blocks[offset];

    internal (Value Slot, VarFlags CombinedFlags, bool IsBlockLocal) GetVarSlot(VarFlags op, int index)
    {
        Debug.Assert(op != VarFlags.None);

        if (Has(op, VarFlags.IsArg)) {
            return (_varSlots[index] ?? _body.Args[index], _varFlags[index], false);
        }
        const VarFlags kBlockLocalFlags = (VarFlags.IsLocal | VarFlags.Loaded | VarFlags.Stored) & ~VarFlags.CrossesBlock;

        var localVar = _ilBody.Locals[index];
        index += _body.Args.Length;

        ref var slot = ref _varSlots[index];
        var flags = _varFlags[index];
        bool isBlockLocal = flags == kBlockLocalFlags && !localVar.IsPinned;

        slot ??= isBlockLocal
            ? (Has(op, VarFlags.Loaded) ? new Undef(localVar.Type) : null)
            : new LocalSlot(
                    localVar.Type, "loc" + index,
                    pinned: localVar.IsPinned,
                    hardExposed: Has(flags, VarFlags.CrossesRegions));

        return (slot!, flags, isBlockLocal);
    }

    internal void SetBlockLocalVarSlot(int index, Value value)
    {
        _varSlots[index + _body.Args.Length] = value;
    }

    internal static (VarFlags Op, int Index) GetVarInstOp(ILCode code, object? operand)
    {
        var op = code switch {
            >= ILCode.Ldarg_0 and <= ILCode.Ldarg_3 => VarFlags.Loaded | VarFlags.IsArg,
            >= ILCode.Ldloc_0 and <= ILCode.Ldloc_3 => VarFlags.Loaded | VarFlags.IsLocal,
            >= ILCode.Stloc_0 and <= ILCode.Stloc_3 => VarFlags.Stored | VarFlags.IsLocal,
            ILCode.Ldarg_S or ILCode.Ldarg          => VarFlags.Loaded | VarFlags.IsArg,
            ILCode.Ldloc_S or ILCode.Ldloc          => VarFlags.Loaded | VarFlags.IsLocal,
            ILCode.Starg_S or ILCode.Starg          => VarFlags.Stored | VarFlags.IsArg,
            ILCode.Stloc_S or ILCode.Stloc          => VarFlags.Stored | VarFlags.IsLocal,
            ILCode.Ldarga_S or ILCode.Ldarga        => VarFlags.AddrTaken | VarFlags.IsArg,
            ILCode.Ldloca_S or ILCode.Ldloca        => VarFlags.AddrTaken | VarFlags.IsLocal,
            _ => VarFlags.None
        };
        int index = op == 0 ? 0 :
            code is >= ILCode.Ldarg_0 and <= ILCode.Stloc_3
                ? (code - ILCode.Ldarg_0) & 3
                : (int)operand!;
        return (op, index);
    }
    private static bool Has(VarFlags x, VarFlags y) => (x & y) != 0;
}