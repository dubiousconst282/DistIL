namespace DistIL.Analysis;

/// <summary> Computes information that can be used to build expression trees from the linear IR. </summary>
public class ForestAnalysis : IMethodAnalysis
{
    // Loosely based on:
    //   "Example stackify algorithm for turning SSA into WASM" - https://gist.github.com/evanw/58a8a5b8b4a1da32fcdcfbf9da87c82a

    // We must track leafs rather than trees because ILGenerator will init
    // ForestAnalysis before RegisterAllocator, which may split critical edges
    // and cause codegen to completely skip instructions in these new blocks.
    readonly RefSet<Instruction> _leafs = new();

    public ForestAnalysis(MethodBody method, AliasAnalysis? aa = null)
    {
        aa ??= new(method);

        foreach (var block in method) {
            // Inline operands, traverse through non-phi instructions in reverse order
            var first = block.FirstNonHeader.Prev;

            for (var inst = block.Last; inst != first; inst = inst.Prev!) {
                if (_leafs.Contains(inst)) continue; // already processed
                InlineOperands(inst, inst, aa);
            }
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new ForestAnalysis(mgr.Method, mgr.GetAnalysis<AliasAnalysis>());

    private void InlineOperands(Instruction inst, Instruction root, AliasAnalysis aa)
    {
        var opers = inst.Operands;

        if (inst is StoreInst && opers[0] is FieldAddrInst or ArrayAddrInst) {
            // For stores, inline the address first to hide hazards, assuming they
            // will be combined into a single CIL instruction.
            InlineOperand(0);
            InlineOperand(1);
            return;
        }

        for (int i = opers.Length - 1; i >= 0; i--) {
            InlineOperand(i);
        }

        void InlineOperand(int i)
        {
            // Check for single-use instruction defined in the same block
            if (inst.Operands[i] is not Instruction oper || oper.Block != inst.Block) return;
            if (oper is PhiInst or FieldInsertInst) return; // needs own slot for codegen
            if (oper.NumUses >= 2 && !IsCheaperToRematerialize(oper)) return;

            if (HasHazardsBetweenDefUse(oper, root, aa)) return;

            // Recursively inline defs, except when rematerializing
            if (_leafs.Add(oper) && oper.NumUses == 1) {
                InlineOperands(oper, root, aa);
            }
        }
    }

    // Checks if `def` can be safely inlined down to a tree at `use`, skipping over leafs.
    private bool HasHazardsBetweenDefUse(Instruction def, Instruction use, AliasAnalysis aa)
    {
        Debug.Assert(def.Block == use.Block);

        if (!def.HasSideEffects && !def.MayReadFromMemory) return false;

        for (var inst = def.Next!; inst != use; inst = inst.Next!) {
            // Assuming that instructions and their operands are inlined in reverse-order,
            // we can safely skip over leafs because they will be evaluated later than `def`.
            if (_leafs.Contains(inst)) continue;

            if (inst.HasSideEffects && !AreInterchangeableHazards(def, inst, aa)) {
                return true;
            }
        }
        return false;
    }

    // Checks if the two instructions have the same hazards and can be executed in either order.
    // This is true if e.g. they may throw the same exception; calls and memory stores are never interchangeable.
    // Assumes that `a` is defined before `b`.
    private bool AreInterchangeableHazards(Instruction a, Instruction b, AliasAnalysis aa)
    {
        // TODO: extend this to query GlobalFunctionEffects for more accurate call info

        // Unrelated memory loads can be safely interchanged
        if (b.MayWriteToMemory) {
            if (a is LoadInst ld && b is StoreInst st) {
                return !aa.MayAlias(ld.Address, st.Address);
            }
            return !a.MayReadFromMemory;
        }
        if (IsAccess(a) && IsAccess(b)) {
            return true;
        }
        return false;

        // May throw one of: NullRef | IndexOutOfBounds | AccessViolation
        // We'll assume it's fine to interchange any of these instructions.
        // TODO: We may want to re-consider this in the future.
        static bool IsAccess(Instruction inst)
            => inst is ArrayAddrInst or FieldAddrInst or LoadInst;
    }

    /// <summary> Checks if the specified instruction is a the root of a tree/statement (i.e. must be emitted and/or assigned into a temp variable). </summary>
    public bool IsTreeRoot(Instruction inst) => !IsLeaf(inst);

    /// <summary> Checks if the specified instruction is a leaf or branch (i.e. can be inlined into an operand). </summary>
    public bool IsLeaf(Instruction inst) => _leafs.Contains(inst);

    /// <summary> Marks the specified instruction as a leaf or root. </summary>
    public void SetLeaf(Instruction inst, bool markAsLeaf)
    {
        if (markAsLeaf) {
            _leafs.Add(inst);
        } else {
            _leafs.Remove(inst);
        }
    }

    // Some instructions are free or cheap enough to make rematerialization preferable to
    // spilling to local vars. RyuJIT doesn't do this as of .NET 8, so at least for instructions
    // with embedded addressing, this should help ILP and avoid increasing register pressure.
    private static bool IsCheaperToRematerialize(Instruction inst)
    {
        bool mayRematerialize =
                (inst is ArrayAddrInst or PtrOffsetInst && inst.NumUses <= 3) ||
                (inst is FieldAddrInst or FieldExtractInst or CilIntrinsic.ArrayLen or CilIntrinsic.SizeOf);

        return mayRematerialize && !inst.Users().Any(u => u is PhiInst); // codegen doesn't support defs inlined into phis, they *must* be rooted
    }
}