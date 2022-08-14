namespace DistIL.IR.Utils;

/// <summary> Computes information to help build expression trees from the linear IR. </summary>
public class Forestifier
{
    readonly ValueSet<Instruction> _trees = new(); //tree instructions / statements

    public Forestifier(MethodBody method)
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

    private bool MustBeRooted(Instruction def, BlockInterfs interfs)
    {
        //Void or unused insts don't need slots
        if (!def.HasResult || def.NumUses == 0) return false;

        //Def must have one use in the same block
        if (def.NumUses >= 2 || def is PhiInst or GuardInst) return true;

        var use = def.GetFirstUser()!;
        return use.Block != def.Block || interfs.IsDefInterferedBeforeUse(def, use);
    }

    /// <summary> Returns whether the specified instruction is a rooted tree or statement (i.e. must be emitted and/or assigned into a temp variable). </summary>
    public bool IsRootedTree(Instruction inst) => _trees.Contains(inst) || !inst.HasResult || (inst.NumUses == 0 && inst.HasSideEffects);

    /// <summary> Returns whether the specified instruction is a leaf (i.e. can be inlined into an operand). </summary>
    public bool IsLeaf(Instruction inst) => !IsRootedTree(inst);

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
                //If this variable is exposed, any store can change its value.
                //Otherwise, we can use precise interferences.
                if (var.IsExposed && _memInterfs.ContainsRange(defIdx, useIdx)) {
                    return true;
                }
                if (_varInterfs.TryGetValue(var, out var localInterfs)) {
                    return localInterfs.ContainsRange(defIdx, useIdx);
                }
                return false;
            }
            //These can be aliased globally, so we can't have precise interferences,
            //or at least not without more extensive tracking.
            if (def is LoadArrayInst or LoadFieldInst or LoadPtrInst) {
                return _memInterfs.ContainsRange(defIdx, useIdx);
            }
            //Assume that instructions with side-effects can interfere with anything else
            if (_sideEffects.ContainsRange(defIdx, useIdx)) {
                foreach (var oper in def.Operands) {
                    if (!IsImmutable(oper)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsImmutable(Value oper)
        {
            //Instructions with more than two uses always have a immutable variable for its entire live range
            return oper is Const or Argument or Instruction { NumUses: >= 2 };
        }
    }
}