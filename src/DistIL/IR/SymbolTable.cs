namespace DistIL.IR;

using System.Runtime.CompilerServices;

/// <summary> Tracks and provides names for values defined/used in a method. </summary>
public class SymbolTable
{
    readonly MethodBody? _method;
    readonly bool _forceSeqNames;

    readonly ConditionalWeakTable<TrackedValue, object /* string | int */> _tags = new();
    readonly HashSet<string> _usedNames = new();
    int _nextId = 1;

    public SymbolTable(MethodBody? method = null, bool forceSeqNames = false)
    {
        _method = method;
        _forceSeqNames = method != null && forceSeqNames;
    }

    public void SetName(TrackedValue value, string name)
    {
        //Pick an unique name
        string origName = name;
        int counter = 2;
        while (!_usedNames.Add(name)) {
            name = origName + counter;
            counter++;
        }
        _tags.AddOrUpdate(value, name);
    }
    
    public bool HasCustomName(TrackedValue value)
    {
        return _tags.TryGetValue(value, out var tag) && tag is string;
    }

    public string GetName(BasicBlock block) => GetName(block, "BB_{0:00}");
    public string GetName(Instruction inst) => GetName(inst, "r{0}");
    public string GetName(Variable var) => GetName(var, "loc{0}");

    private string GetName(TrackedValue value, string format)
    {
        if (!_tags.TryGetValue(value, out var tag)) {
            if (_forceSeqNames && value is Instruction or BasicBlock) {
                UpdateSeqNames();
                _tags.TryGetValue(value, out tag);
            }
            if (tag == null) {
                tag = _nextId++;
                _tags.Add(value, tag);
            }
        }
        return tag as string ?? string.Format(format, tag);
    }
    private void UpdateSeqNames()
    {
        int currId = 1;
        foreach (var block in _method!) {
            AddNext(block);

            foreach (var inst in block) {
                AddNext(inst);
            }
        }
        void AddNext(TrackedValue value)
        {
            if (!HasCustomName(value)) {
                _tags.AddOrUpdate(value, currId++);
            }
        }
    }
}

public static class SymbolTableEx
{
    /// <summary> Sets the instruction name on its parent symbol table. </summary>
    public static TInst SetName<TInst>(this TInst inst, string name) where TInst : Instruction
    {
        inst.GetSymbolTable()!.SetName(inst, name);
        return inst;
    }
    /// <summary> Sets the block name on its parent symbol table. </summary>
    public static BasicBlock SetName(this BasicBlock block, string name)
    {
        block.GetSymbolTable()!.SetName(block, name);
        return block;
    }
    /// <summary> Sets the value name on its parent symbol table (if it is a <see cref="TrackedValue"/>). </summary>
    public static Value SetName(this Value value, string name)
    {
        if (value is TrackedValue trackedValue) {
            value.GetSymbolTable()?.SetName(trackedValue, name);
        }
        return value;
    }
}