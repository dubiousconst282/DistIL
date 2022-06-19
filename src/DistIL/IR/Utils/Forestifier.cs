namespace DistIL.IR.Utils;

/// <summary> Computes information to help build expression trees from the linear IR. </summary>
public class Forestifier
{
    readonly MethodBody _method;
    readonly Dictionary<Instruction, Variable?> _slots = new();

    public Forestifier(MethodBody method)
    {
        _method = method;

        var interfs = new BlockInterfs();
        int slotId = 1;

        foreach (var block in method) {
            //Update block interferences and calculate instruction indices
            interfs.Update(block);

            //Create instruction slots
            foreach (var inst in block) {
                if (inst.HasResult && inst.NumUses > 0 && NeedsSlot(inst, interfs)) {
                    _slots[inst] = new Variable(inst.ResultType, name: $"expr{slotId++}");
                }
            }
        }
    }

    private bool NeedsSlot(Instruction def, BlockInterfs interfs)
    {
        //Def must have one use in the same block
        if (def.NumUses >= 2) return true;

        var use = def.GetFirstUser()!;
        if (use.Block != def.Block) return true;

        return interfs.IsDefInterferedBeforeUse(def, use);
    }

    public (ExprKind Kind, Variable? Slot) GetNode(Instruction inst)
    {
        var slot = _slots.GetValueOrDefault(inst);
        var kind =
            slot != null || !inst.HasResult ? ExprKind.Stmt :
            inst.NumUses == 0 && inst.HasResult ? ExprKind.UnusedStmt :
            ExprKind.Leaf;
        return (kind, slot);
    }

    public bool TryGetSlot(Instruction inst, [MaybeNullWhen(false)] out Variable slot)
    {
        return _slots.TryGetValue(inst, out slot);
    }

    public bool IsLeaf(Instruction inst)
    {
        return !_slots.ContainsKey(inst);
    }

    class BlockInterfs
    {
        Dictionary<Instruction, int> _indices = new();
        Dictionary<Variable, BitSet> _varInterfs = new();
        BitSet _aliasInterfs = new BitSet();
        BitSet _sideEffects = new BitSet();

        public void Update(BasicBlock block)
        {
            _indices.Clear();
            _varInterfs.Clear();
            _aliasInterfs.Clear();
            _sideEffects.Clear();

            int index = 0;
            foreach (var inst in block) {
                if (inst is StoreVarInst store) {
                    var set = _varInterfs.GetOrAddRef(store.Var) ??= new();
                    set.Add(index);
                }
                //Check whether this instruction may change a alias
                //- StorePtrInst
                //- Calls with a ref/ptr argument (TODO)
                else if (inst.MayWriteToMemory) {
                    _aliasInterfs.Add(index);
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
                if (var.IsExposed && _aliasInterfs.ContainsRange(defIdx, useIdx)) {
                    return true;
                }
                if (_varInterfs.TryGetValue(var, out var localInterfs)) {
                    return localInterfs.ContainsRange(defIdx, useIdx);
                }
                return false;
            }
            if (def is LoadArrayInst or LoadFieldInst or LoadPtrInst) {
                return _aliasInterfs.ContainsRange(defIdx, useIdx);
            }
            //Check if operands may be interfered by instructions with side-effects
            //(they could intefere with anything (fields, pointers, ...))
            foreach (var oper in def.Operands) {
                if (MayBeInterfered(oper) && _sideEffects.ContainsRange(defIdx, useIdx)) {
                    return true;
                }
            }
            return false;
        }

        private bool MayBeInterfered(Value oper)
        {
            //Instructions with more than two uses always have a immutable slot
            return !(oper is Const or Instruction { NumUses: >= 2 });
        }
    }
}
public enum ExprKind { UnusedStmt, Stmt, Leaf }