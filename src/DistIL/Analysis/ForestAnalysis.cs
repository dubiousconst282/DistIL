namespace DistIL.Analysis;

/// <summary> Computes information that can be used to build expression trees from the linear IR. </summary>
public class ForestAnalysis : IMethodAnalysis
{
    readonly RefSet<Instruction> _trees = new(); //expr tree roots / statements

    public ForestAnalysis(MethodBody method)
    {
        var interfs = new BlockInterfs();

        foreach (var block in method) {
            //Update block interferences and calculate instruction indices
            interfs.Update(block);

            //Find statements
            foreach (var inst in block) {
                if (MustBeRooted(inst, interfs)) {
                    _trees.Add(inst);
                }
            }
        }
    }

    public static IMethodAnalysis Create(IMethodAnalysisManager mgr)
    {
        return new ForestAnalysis(mgr.Method);
    }

    private static bool MustBeRooted(Instruction def, BlockInterfs interfs)
    {
        //Consider void or unused instructions on demand, to make the set smaller.
        if (!def.HasResult || def.NumUses == 0 || CheapToRematerialize(def)) return false;

        //Def must have one use in the same block, with no interferences in between def and use
        if (def.NumUses >= 2 || def is PhiInst or GuardInst) return true;

        var use = def.GetFirstUser()!;
        return use.Block != def.Block || interfs.IsDefInterferedBeforeUse(def, use);
    }

    private static bool CheapToRematerialize(Instruction inst)
    {
        return inst is VarAddrInst or ArrayLenInst;
    }

    /// <summary> Returns whether the specified instruction is a the root of a tree/statement (i.e. must be emitted and/or assigned into a temp variable). </summary>
    public bool IsTreeRoot(Instruction inst) 
        => _trees.Contains(inst) || !inst.HasResult || (inst.NumUses == 0 && inst.HasSideEffects);

    /// <summary> Returns whether the specified instruction is a leaf or branch (i.e. can be inlined into an operand). </summary>
    public bool IsLeaf(Instruction inst) => !IsTreeRoot(inst);

    class BlockInterfs
    {
        Dictionary<Instruction, int> _indices = new();
        Dictionary<Variable, BitSet> _varInterfs = new();
        BitSet _memInterfs = new();
        BitSet _sideEffects = new();

        public void Update(BasicBlock block)
        {
            _indices.Clear();
            _varInterfs.Clear();
            _memInterfs.Clear();
            _sideEffects.Clear();

            int index = 0;
            foreach (var inst in block) {
                if (inst is StoreVarInst store) {
                    var set = _varInterfs.GetOrAddRef(store.Var) ??= new();
                    set.Add(index);
                } else if (inst.MayWriteToMemory) {
                    _memInterfs.Add(index);
                }

                if (inst.HasSideEffects) {
                    _sideEffects.Add(index);
                }
                _indices[inst] = index;
                index++;
            }
        }

        public bool IsDefInterferedBeforeUse(Instruction def, Instruction use)
        {
            int defIdx = _indices[def] + 1; //offset by one to ignore def when checking for interferences
            int useIdx = _indices[use];

            if (def is LoadVarInst { Var: var var }) {
                //If this variable is exposed, assume that any store can change its value
                if (var.IsExposed && _memInterfs.ContainsRange(defIdx, useIdx)) {
                    return true;
                }
                //...otherwise, we can use precise interferences
                if (_varInterfs.TryGetValue(var, out var localInterfs)) {
                    return localInterfs.ContainsRange(defIdx, useIdx);
                }
                return false;
            }
            //Like exposed variables, these can be aliased globally and requires
            //something like alias analysis for precise results, which we don't have yet.
            if (def is LoadArrayInst or LoadFieldInst or LoadPtrInst) {
                return _memInterfs.ContainsRange(defIdx, useIdx);
            }
            //Assume that instructions with side-effects can interfere with anything else
            if (_sideEffects.ContainsRange(defIdx, useIdx)) {
                foreach (var oper in def.Operands) {
                    if (!IsInvariant(oper)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsInvariant(Value oper)
        {
            //Instructions with more than two uses always have a invariant variable for its entire live range
            return oper is Const or Argument or Instruction { NumUses: >= 2 };
        }
    }
}