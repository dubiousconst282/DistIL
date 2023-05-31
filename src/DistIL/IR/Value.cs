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
    //In order to minimize GC stress and memory overhead, the value use-chain is implemented by a special
    //doubly-linked list using pointers represented as UseRef, a tuple of (Instruction Parent, int OperIndex).
    //The previous and next links are keept in `Instruction._useDefs`.
    UseRef _firstUse;

    /// <summary> The number of (operand) uses this value have. </summary>
    public int NumUses { get; private set; }

    internal override void AddUse(Instruction user, int operIdx)
    {
        var node = new UseRef(user, operIdx);
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
        var node = new UseRef(user, operIdx);

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

    /// <summary> Replace all uses of this value with <paramref name="newValue"/>. </summary>
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
            node.OperandRef = dest;
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
            node.OperandRef = dest;
            node.Def = default; //erase node slot to prevent "ghost" links
            node = next;
        }
    }

    protected TrackedValue MemberwiseDetachedClone()
    {
        var val = (TrackedValue)MemberwiseClone();
        val._firstUse = default;
        val.NumUses = 0;
        return val;
    }

    /// <summary> Returns an iterator of instructions using this value. Neither order nor uniqueness is guaranteed. </summary>
    public ValueUserIterator Users() => new() { _use = _firstUse };
    /// <summary> Returns an iterator of operands using this value. </summary>
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
            Current = _use.Parent;
            _use = _use.Next;
            return true;
        }
        return false;
    }
}
public struct ValueUseIterator : Iterator<UseRef>
{
    internal UseRef _use;
    public UseRef Current { get; private set; }

    public bool MoveNext()
    {
        if (_use.Exists) {
            Current = _use;
            _use = _use.Next;
            return true;
        }
        return false;
    }
}

/// <summary> Represents a reference to an instruction operand. </summary>
public readonly struct UseRef
{
    public Instruction Parent { get; }
    public int OperIndex { get; }

    internal UseRef(Instruction inst, int operIdx)
        => (Parent, OperIndex) = (inst, operIdx);

    public bool Exists => Parent != null;

    internal ref UseRef Prev => ref Def.Prev;
    internal ref UseRef Next => ref Def.Next;

    internal ref UseDef Def => ref Parent._useDefs[OperIndex];

    internal ref Value OperandRef => ref Parent._operands[OperIndex];

    public Value Operand {
        get => Parent._operands[OperIndex];
        set => Parent.ReplaceOperand(OperIndex, value);
    }

    public void Deconstruct(out Instruction inst, out int operIdx)
        => (inst, operIdx) = (Parent, OperIndex);

    public override string ToString() => Parent == null ? "null" : $"[{OperIndex}] at '{Parent}'";
}
/// <summary> Represents the "definition" of a value use. Contents should only be modified by <see cref="TrackedValue" />. </summary>
internal struct UseDef
{
    public UseRef Prev, Next;
}