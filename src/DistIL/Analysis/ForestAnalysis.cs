namespace DistIL.Analysis;

/// <summary> Computes information that can be used to build expression trees from the linear IR. </summary>
public class ForestAnalysis : IMethodAnalysis
{
    //Note that we must track leafs rather than trees because the codegen will
    //init the ForestAnalysis before RegisterAllocator, which may split critical edges
    //and cause codegen to completely skip instructions in these new blocks.
    readonly RefSet<Instruction> _leafs = new();

    public ForestAnalysis(MethodBody method)
    {
        //This is a simple forward use scan algorithm, perhaps a even simpler approach
        //would be to simulate a eval stack, pushing instructions as they appear, and
        //popping operands while "dropping" those that don't match or were interfered.
        var interfs = new BlockInterfs();

        foreach (var block in method) {
            //Update block interferences and calculate instruction indices
            interfs.Update(block);

            //Find statements
            foreach (var inst in block) {
                if (interfs.CanBeInlinedIntoUse(inst)) {
                    _leafs.Add(inst);
                }
            }
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new ForestAnalysis(mgr.Method);

    /// <summary> Returns whether the specified instruction is a the root of a tree/statement (i.e. must be emitted and/or assigned into a temp variable). </summary>
    public bool IsTreeRoot(Instruction inst) => !IsLeaf(inst);

    /// <summary> Returns whether the specified instruction is a leaf or branch (i.e. can be inlined into an operand). </summary>
    public bool IsLeaf(Instruction inst) => _leafs.Contains(inst);


    private static bool IsAlwaysRooted(Instruction inst)
    {
        if (!inst.HasResult || inst.NumUses is 0 or >= 2 || inst is PhiInst or GuardInst) {
            return true;
        }
        var (user, useIdx) = inst.Uses().First();

        return user.Block != inst.Block ||
               user is PhiInst ||
               (user is SelectInst && useIdx != 0); //ILGenerator depends on select values being roots, for simplicity reaon
    }

    private static bool IsAlwaysLeaf(Instruction inst)
    {
        //Cheaper to rematerialize
        return (inst is CilIntrinsic.SizeOf or CilIntrinsic.ArrayLen) ||
               (inst is FieldAddrInst fa && fa.Obj is null or LocalSlot or Argument or Instruction { NumUses: >= 2 });
    }

    class BlockInterfs
    {
        Dictionary<Instruction, int> _indices = new();
        BitSet _memInterfs = new();
        BitSet _sideEffects = new();

        public void Update(BasicBlock block)
        {
            _indices.Clear();
            _memInterfs.Clear();
            _sideEffects.Clear();

            int index = 0;
            foreach (var inst in block) {
                if (inst.MayWriteToMemory) {
                    _memInterfs.Add(index);
                }
                if (inst.HasSideEffects) {
                    _sideEffects.Add(index);
                }
                _indices[inst] = index;
                index++;
            }
        }

        public bool CanBeInlinedIntoUse(Instruction inst)
        {
            return IsAlwaysLeaf(inst) || (!IsAlwaysRooted(inst) && !IsDefInterferedBeforeUse(inst));
        }

        private bool IsDefInterferedBeforeUse(Instruction def)
        {
            int defIdx = _indices[def] + 1; //offset by one to ignore def when checking for interferences
            int useIdx = GetLastSafeUsePoint(def);

            //Like exposed variables, these can be aliased globally and we need
            //something like alias analysis for precise results.
            if (def is LoadInst) {
                return _memInterfs.ContainsRange(defIdx, useIdx);
            }
            //Consider:
            //  int r1 = call Foo() //may throw
            //  int r2 = call Bar() //can't be inlined
            //  int r3 = add r1, r2
            //If we inlined r1 into r3, Bar() would be called before Foo().
            if (_sideEffects.ContainsRange(defIdx, useIdx)) {
                return true;
            }
            return false;
        }

        private int GetLastSafeUsePoint(Instruction def)
        {
            //If `inst` operands are ordered in the same way as they are evaluated in an expression,
            //we only need to check for side effects up to the next operand def.
            //  int r1 = call A()
            //  int r2 = call B()           //last safe point for r1 (if r2 can also be inlined)
            //  int r3 = call C(r1, r2)
            var (use, operIdx) = def.Uses().First();
            int pos = _indices[use];

            if (HasConsistentEvalOrder(use)) {
                int minPos = _indices[def] + 1;

                for (int i = operIdx + 1; i < use.Operands.Length; i++) {
                    if (use.Operands[i] is Instruction oper && oper.Block == use.Block && _indices[oper] >= minPos) {
                        return CanBeInlinedIntoUse(oper) ? _indices[oper] : pos;
                    }
                }
            }
            return pos;
        }

        //Returns whether the instruction operands are ordered in the same way as they would be pushed on the stack
        private static bool HasConsistentEvalOrder(Instruction inst)
        {
            return inst is not (IntrinsicInst or SwitchInst);
        }
    }
}