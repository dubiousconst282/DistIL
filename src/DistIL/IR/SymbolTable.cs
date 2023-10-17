namespace DistIL.IR;

using System.Runtime.CompilerServices;

/// <summary> Tracks and provides names for values defined/used in a method. </summary>
public class SymbolTable
{
    /// <summary> A read-only symbol table which is not attached to any method, and only provides a static dummy name. </summary>
    public static SymbolTable Empty { get; } = new();

    readonly MethodBody? _method;

    readonly ConditionalWeakTable<TrackedValue, object /* string | int */> _tags = new();
    readonly HashSet<string> _usedNames = new();
    int _nextId = 1;

    public SymbolTable(MethodBody? method = null)
    {
        _method = method;
    }

    public void SetName(TrackedValue value, string name)
    {
        Ensure.That(this != Empty);

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
    public string GetName(LocalSlot var) => GetName(var, "loc{0}");

    private string GetName(TrackedValue value, string format)
    {
        if (this == Empty) {
            return string.Format(format, 0);
        }
        if (!_tags.TryGetValue(value, out var tag)) {
            if (_nextId == 1 && value is Instruction or BasicBlock) {
                AssignInitialNames();
                _tags.TryGetValue(value, out tag);
            }
            if (tag == null) {
                tag = _nextId++;
                _tags.Add(value, tag);
            }
        }
        return tag as string ?? string.Format(format, tag);
    }
    private void AssignInitialNames()
    {
        foreach (var block in _method!) {
            AddNext(block);

            foreach (var inst in block) {
                AddNext(inst);
            }
        }
        void AddNext(TrackedValue value)
        {
            if (!HasCustomName(value)) {
                _tags.AddOrUpdate(value, _nextId++);
            }
        }
    }
}

public static class SymbolTableExt
{
    /// <summary> Sets the name of a value if it is attached to a symbol table, otherwise do nothing. </summary>
    public static V SetName<V>(this V value, string name) where V : Value
    {
        if (value is TrackedValue trackedValue) {
            trackedValue.GetSymbolTable()?.SetName(trackedValue, name);
        }
        return value;
    }
}