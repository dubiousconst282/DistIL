namespace DistIL.IR;

/// <summary> Tracks and provides names for values defined/used in a method. </summary>
public class SymbolTable
{
    Dictionary<TrackedValue, int> _slots = new();
    Dictionary<TrackedValue, string> _names = new();
    HashSet<string> _usedNames = new();

    public void UpdateSlots(MethodBody method)
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
        //Delete names of removed values
        foreach (var (val, name) in _names) {
            if (!_slots.ContainsKey(val)) {
                _names.Remove(val);
                _usedNames.Remove(name);
            }
        }
    }
    private void Add(TrackedValue value)
    {
        _slots.Add(value, _slots.Count + 1);
    }

    public void SetName(TrackedValue value, string? name)
    {
        if (name == null) {
            if (_names.Remove(value, out var prevName)) {
                _usedNames.Remove(prevName);
            }
            return;
        }
        //Pick a unique name
        string origName = name;
        int counter = 2;
        while (!_usedNames.Add(name)) {
            name = origName + counter;
            counter++;
        }
        _names[value] = name;
    }
    
    public bool HasCustomName(TrackedValue value)
    {
        return _names.ContainsKey(value);
    }

    public string GetName(Instruction inst)
        => GetName(inst, slot => $"r{slot}", unkName: "r?");

    public string GetName(Variable var)
        => GetName(var, slot => $"loc{slot}");

    public string GetName(BasicBlock block)
        => GetName(block, slot => $"BB_{slot:00}", unkName: "BB_??");

    private string GetName(TrackedValue value, Func<int/*Slot*/, string> genAnon, string? unkName = null)
    {
        if (_names.TryGetValue(value, out var name)) {
            return name;
        }
        if (!_slots.TryGetValue(value, out int id)) {
            if (unkName != null) {
                return unkName;
            }
            _slots[value] = id = _slots.Count + 1;
        }
        return genAnon(id);
    }
}
public static class SlotTrackerEx
{
    /// <summary> Sets the instruction name on its parent method slot tracker. </summary>
    public static TInst SetName<TInst>(this TInst inst, string name) where TInst : Instruction
    {
        inst.Block.Method.GetSymbolTable().SetName(inst, name);
        return inst;
    }
    /// <summary> Sets the block name on its parent method slot tracker. </summary>
    public static BasicBlock SetName(this BasicBlock block, string name)
    {
        block.Method.GetSymbolTable().SetName(block, name);
        return block;
    }
}