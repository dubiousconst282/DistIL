namespace DistIL.IR;

using System.Runtime.CompilerServices;

public abstract class Value
{
    public TypeDesc ResultType { get; protected set; } = PrimType.Void;
    /// <summary> Whether this value's result type is not void. </summary>
    public bool HasResult => ResultType.Kind != TypeKind.Void;

    public abstract void Print(StringBuilder sb, SlotTracker slotTracker);
    public virtual void PrintAsOperand(StringBuilder sb, SlotTracker slotTracker) => Print(sb, slotTracker);
    protected virtual SlotTracker GetDefaultSlotTracker() => new();

    public override string ToString()
    {
        var sb = new StringBuilder();
        Print(sb, GetDefaultSlotTracker());
        return sb.ToString();
    }

    internal virtual void AddUse(Use use) { }
    internal virtual void RemoveUse(Use use) { }
}
/// <summary> The base class for a value that tracks it uses. </summary>
public abstract class TrackedValue : Value
{
    //Using a linked list to track uses allows new uses to be added/removed during enumeration
    internal Use? _firstUse;

    //The value hash is calculated based on the object address on the constructor.
    //It should help a bit since object.GetHashCode() is a virtual call to a runtime
    //function, which seems to be doing some quite expansive stuff the first time it's called.
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal readonly int _hash;

    /// <summary> The number of (operand) uses this value have. </summary>
    public int NumUses { get; private set; }

    public TrackedValue()
    {
        _hash = GetAddrHash(this);
    }

    internal override void AddUse(Use use)
    {
        Assert(GetType() != typeof(Variable) || use.User is LoadVarInst or StoreVarInst or VarAddrInst);

        if (_firstUse != null) {
            _firstUse.Prev = use;
            use.Next = _firstUse;
        }
        _firstUse = use;
        NumUses++;
    }
    internal override void RemoveUse(Use use)
    {
        if (use.Prev != null) {
            use.Prev.Next = use.Next;
        } else {
            _firstUse = use.Next!;
        }
        if (use.Next != null) {
            use.Next.Prev = use.Prev;
        }
        use.Prev = use.Next = null;
        NumUses--;
    }

    public Instruction? GetFirstUser()
    {
        return _firstUse?.User;
    }

    /// <summary> Replace all uses of this value with `newValue`. </summary>
    public void ReplaceUses(Value newValue)
    {
        if (newValue == this || _firstUse == null) return;

        //Update user operands and find last use
        var use = _firstUse;
        while (true) {
            use.User._operands[use.OperIdx] = newValue;
            if (use.Next == null) break;
            use = use.Next;
        }
        //Merge use lists
        if (newValue is TrackedValue n) {
            if (n._firstUse != null) {
                use.Next = n._firstUse;
                n._firstUse.Prev = use;
            }
            n._firstUse = _firstUse;
            n.NumUses += NumUses;
        }
        _firstUse = null;
        NumUses = 0;
    }

    public override int GetHashCode() => _hash;
    private static int GetAddrHash(object obj)
    {
        //This is a Fibonacci hash. It's very fast, compact, and generates satisfactory results.
        //The result must be cached, because it will change when the GC compacts the heap.
        ulong addr = Unsafe.As<object, ulong>(ref obj); // *&obj
        return (int)((addr * 11400714819323198485) >> 32);
    }

    /// <summary> Returns an enumerator of instructions using this value. Neither order nor uniqueness is guaranteed. </summary>
    public ValueUserEnumerator Users() => new() { _use = _firstUse };
    /// <summary> Returns an enumerator of operands using this value. </summary>
    public ValueUseEnumerator Uses() => new() { _use = _firstUse };
}

public struct ValueUserEnumerator
{
    internal Use? _use;
    public Instruction Current { get; private set; }

    public bool MoveNext()
    {
        if (_use != null) {
            Current = _use.User;
            _use = _use.Next;
            return true;
        }
        return false;
    }

    public ValueUserEnumerator GetEnumerator() => this;
}
public struct ValueUseEnumerator
{
    internal Use? _use;
    public (Instruction Inst, int OperIdx) Current { get; private set; }

    public bool MoveNext()
    {
        if (_use != null) {
            Current = (_use.User, _use.OperIdx);
            _use = _use.Next;
            return true;
        }
        return false;
    }

    public ValueUseEnumerator GetEnumerator() => this;
}
internal class Use
{
    public Use? Prev, Next;
    public Instruction User = null!;
    public int OperIdx;
    //24(obj hdr)+28(fields)+8(oper ref) = 60 bytes + GC stress
    //Not that bad. If we need to optimize memory usage, we could use some kind of allocator/pool of use nodes
    //addressed by ints, which would bring this down to 12+8+4(oper ref) = 24 bytes and reduce GC stress.
}