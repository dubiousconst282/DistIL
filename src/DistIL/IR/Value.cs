namespace DistIL.IR;

using System.IO;

public abstract class Value
{
    public TypeDesc ResultType { get; protected set; } = PrimType.Void;
    /// <summary> Whether this value's result type is not void. </summary>
    public bool HasResult => ResultType.Kind != TypeKind.Void;

    public abstract void Print(PrintContext ctx);
    public virtual void PrintAsOperand(PrintContext ctx) => Print(ctx);

    public override string ToString()
    {
        var sw = new StringWriter();
        Print(new PrintContext(sw, (this as TrackedValue)?.GetSymbolTable() ?? SymbolTable.Detached));
        return sw.ToString();
    }

    internal virtual void AddUse(Instruction user, int operIdx) { }
    internal virtual void RemoveUse(Instruction user, int operIdx) { }
}

/// <summary> The base class for a value that tracks it uses. </summary>
public abstract class TrackedValue : Value
{
    //The value use chain is represented by a doubly-linked list, pointers are represented
    //as (Inst Owner, int Index), which index `Instruction._useDefs`.
    UseRef _firstUse;

    /// <summary> The number of (operand) uses this value have. </summary>
    public int NumUses { get; private set; }

    internal override void AddUse(Instruction user, int operIdx)
    {
        var node = new UseRef() { Owner = user, Index = operIdx };
        Debug.Assert(!node.Prev.Exists && !node.Next.Exists);

        if (_firstUse.Exists) {
            _firstUse.Prev = node;
            node.Next = _firstUse;
        }
        _firstUse = node;
        NumUses++;
    }
    internal override void RemoveUse(Instruction user, int operIdx)
    {
        var node = new UseRef() { Owner = user, Index = operIdx };

        if (node.Prev.Exists) {
            node.Prev.Next = node.Next;
        } else {
            _firstUse = node.Next;
        }
        if (node.Next.Exists) {
            node.Next.Prev = node.Prev;
        }
        node.Def = default;
        NumUses--;
    }

    /// <summary> Replace all uses of this value with `newValue`. </summary>
    public void ReplaceUses(Value newValue)
    {
        if (newValue == this || !_firstUse.Exists) return;

        if (newValue is TrackedValue newTrackedValue) {
            TransferUses(newTrackedValue);
        } else {
            TransferUsesToUntracked(newValue);
        }
        _firstUse = default;
        NumUses = 0;
    }

    private void TransferUses(TrackedValue dest)
    {
        var lastNode = default(UseRef);
        for (var node = _firstUse; node.Exists; node = node.Next) {
            node.Operand = dest;
            lastNode = node;
        }
        //Append the uselist from the newValue at the end of this one, and transfer ownership
        if (dest._firstUse.Exists) {
            Debug.Assert(!dest._firstUse.Prev.Exists);

            dest._firstUse.Prev = lastNode;
            lastNode.Next = dest._firstUse;
        }
        dest._firstUse = _firstUse;
        dest.NumUses += NumUses;
    }
    private void TransferUsesToUntracked(Value dest)
    {
        for (var node = _firstUse; node.Exists; ) {
            var next = node.Next;
            node.Operand = dest;
            node.Def = default; //erase node slot to avoid "ghost" links
            node = next;
        }
    }

    /// <summary> Returns an enumerator of instructions using this value. Neither order nor uniqueness is guaranteed. </summary>
    public ValueUserIterator Users() => new() { _use = _firstUse };
    /// <summary> Returns an enumerator of operands using this value. </summary>
    public ValueUseIterator Uses() => new() { _use = _firstUse };

    public virtual SymbolTable? GetSymbolTable() => null;
}

public struct ValueUserIterator : Iterator<Instruction>
{
    internal UseRef _use;
    public Instruction Current { get; private set; }

    public bool MoveNext()
    {
        if (_use.Exists) {
            Current = _use.Owner;
            _use = _use.Next;
            return true;
        }
        return false;
    }
}
public struct ValueUseIterator : Iterator<(Instruction Inst, int OperIdx)>
{
    internal UseRef _use;
    public (Instruction Inst, int OperIdx) Current { get; private set; }

    public bool MoveNext()
    {
        if (_use.Exists) {
            Current = (_use.Owner, _use.Index);
            _use = _use.Next;
            return true;
        }
        return false;
    }
}

internal struct UseRef
{
    public Instruction Owner;
    public int Index;

    public bool Exists => Owner != null;

    public ref UseRef Prev => ref Def.Prev;
    public ref UseRef Next => ref Def.Next;

    public ref UseDef Def => ref Owner._useDefs[Index];
    public ref Value Operand => ref Owner._operands[Index];

    public override string ToString() => Owner == null ? "<null>" : $"<{Owner}> at {Index}";
}
internal struct UseDef
{
    public UseRef Prev, Next;
}