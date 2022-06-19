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

        int defIdx = interfs.GetIndex(def) + 1; //offset by one to ignore def when checking for interferences
        int useIdx = interfs.GetIndex(use);

        //Operands can't have interferes before use
        foreach (var oper in def.Operands) {
            if (interfs.IsInterferedBetween(oper, defIdx, useIdx)) {
                return true;
            }
        }
        return false;
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
                //- Instructions that may throw could interfere with aliases/vars crossing protected regions
                //- Calls with a ref/ptr argument (TODO)
                else if (inst.MayThrow || inst is StorePtrInst or CallInst) {
                    _aliasInterfs.Add(index);
                }

                if (inst.HasSideEffects) {
                    _sideEffects.Add(index);
                }
                _indices[inst] = index;
                index++;
            }
        }

        public int GetIndex(Instruction inst) => _indices[inst];

        public bool IsInterferedBetween(Value oper, int start, int end)
        {
            if (oper is LoadVarInst load) {
                oper = load.Var;
            }
            switch (oper) {
                case Const: {
                    //Constants are never interfered with
                    return false;
                }
                case Variable var: {
                    if (var.IsExposed && _aliasInterfs.ContainsRange(start, end)) {
                        return true;
                    }
                    if (_varInterfs.TryGetValue(var, out var localInterfs)) {
                        return localInterfs.ContainsRange(start, end);
                    }
                    return false;
                }
                default: {
                    //Instructions with side-effects could intefere with anything (fields, pointers, ...)
                    return _sideEffects.ContainsRange(start, end);
                }
            }
        }
    }
}
public enum ExprKind { UnusedStmt, Stmt, Leaf }