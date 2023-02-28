namespace DistIL.Passes.Vectorization;

internal class VectorTreeStamper
{
    readonly IndexMap<Instruction> _insts = new();
    readonly BitSet _fibers = new();
    readonly BitSet _sideEffects = new();

    int _treeMin = int.MaxValue, _treeMax = 0;

    public void AddInst(Instruction inst)
    {
        int index = _insts.Add(inst);

        if (inst.HasSideEffects) {
            _sideEffects.Add(index);
        }
    }

    public void Reset()
    {
        _insts.Clear();
        _sideEffects.Clear();
        ResetTree(removeFibers: false);
    }

    public void ResetTree(bool removeFibers)
    {
        if (removeFibers) {
            foreach (int idx in _fibers) {
                _insts.At(idx).Remove();
            }
        }
        _fibers.Clear();
    }

    public (VectorNode Root, float EstimCost) BuildTree(VectorType type, Value[] lanes)
    {
        var builder = new VectorTreeBuilder() {
            Stamper = this,
            VecType = type
        };
        var root = builder.BuildTree(lanes);
        //FIXME: Capture leaf loads and check transform legality
        return (root, builder.Cost);
    }

    //Schedules associated scalar lanes to be removed.
    public VectorNode TieFibers(VectorNode node, Value[] lanes)
    {
        foreach (var lane in lanes) {
            if (lane is Instruction inst) {
                int index = _insts.IndexOf(inst);
                _fibers.Add(index);

                _treeMin = Math.Min(_treeMin, index);
                _treeMax = Math.Max(_treeMax, index);
            }
        }
        return node;
    }

    public bool Contains(Instruction inst)
    {
        return _insts.Contains(inst);
    }
    
    public bool IsLegalLoad(Value address, VectorType type)
    {
        foreach (int index in _sideEffects.GetRangeEnumerator(_treeMin, _treeMax + 1)) {
            if (_insts.At(index) is not StorePtrInst st || 
                AddressesMayOverlap(address, st.Address, Math.Max(st.ElemType.Kind.BitSize(), type.BitWidth) / 8)
            ) {
                return true;
            }
        }
        return false;
    }

    private bool AddressesMayOverlap(Value addr1, Value addr2, int width)
    {
        return false;
    }
}