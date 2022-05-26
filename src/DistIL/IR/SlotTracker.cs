namespace DistIL.IR;

/// <summary> Provides ids/names for values in a method. </summary>
public class SlotTracker
{
    private Dictionary<Value, int> _slots = new();

    public void Update(MethodBody method)
    {
        _slots.Clear();

        foreach (var bb in method) {
            Add(bb);

            foreach (var inst in bb) {
                if (inst.HasResult) {
                    Add(inst);
                }
            }
        }
    }
    
    private void Add(Value value)
    {
        _slots.Add(value, _slots.Count + 1);
    }

    public void Clear()
    {
        _slots.Clear();
    }

    public int? GetId(Instruction inst) => GetOrNull(inst);
    public int GetId(Variable var) => GetOrCreate(var);
    public int GetId(BasicBlock block) => GetOrCreate(block);

    private int? GetOrNull(Value value)
    {
        return _slots.TryGetValue(value, out int id) ? id : null;
    }
    private int GetOrCreate(Value value)
    {
        if (!_slots.TryGetValue(value, out int id)) {
            _slots[value] = id = _slots.Count + 1;
        }
        return id;
    }
}