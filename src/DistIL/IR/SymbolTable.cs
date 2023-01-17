namespace DistIL.IR;

using System.Runtime.CompilerServices;

/// <summary> Tracks and provides names for values defined/used in a method. </summary>
public class SymbolTable
{
    /// <summary> A read-only symbol table which is not attached to any method, and only provides a static dummy name. </summary>
    public static SymbolTable Detached { get; } = new();

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
        Ensure.That(this != Detached);

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
        if (this == Detached) {
            return string.Format(format, 0);
        }
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
                _tags.AddOrUpdate(value, currId);
            }
            currId++;
        }
    }
}

public static class SymbolTableExt
{
    /// <summary> Sets the value name on its parent symbol table (if it is a <see cref="TrackedValue"/>). </summary>
    public static V SetName<V>(this V value, string name) where V : Value
    {
        if (value is TrackedValue trackedValue) {
            trackedValue.GetSymbolTable()?.SetName(trackedValue, name);
        }
        return value;
    }
}