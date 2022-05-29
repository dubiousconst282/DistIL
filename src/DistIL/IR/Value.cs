namespace DistIL.IR;

using System.Runtime.CompilerServices;

using User = Instruction;
using UserSet = ValueSet<Instruction>;

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

    internal virtual void AddUse(User user, int operIdx) { }
    internal virtual void RemoveUse(User user, int operIdx, bool removeEvenIfStillBeingUsed = false) { }
}
/// <summary> The base class for a value that tracks it uses. </summary>
public abstract class TrackedValue : Value
{
    //Uses are tracked using a custom linear probing hash set (ValueSet),
    //which is only allocated if there are more than one user.
    //In the future, we could also track the first 32 operand indices with an int bit set,
    //but since most instructions have 1-2 operands, it may not be worth the extra complexity/overhead.
    internal object _users = null!;
    int _numUses;

    //The value hash is calculated based on the object address on the constructor.
    //It should help a bit since object.GetHashCode() is a virtual call to a runtime
    //function, which seems to be doing some quite expansive stuff the first time it's called.
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal readonly int _hash;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    User _singleUser {
        get {
            Assert(_users is User);
            return Unsafe.As<User>(_users);
        }
    }
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    UserSet _userSet {
        get {
            Assert(_users.GetType() == typeof(UserSet));
            return Unsafe.As<UserSet>(_users);
        }
    }

    /// <summary> The number of distinct instructions using this value. </summary>
    public int NumUsers =>
        _users == null ? 0 :
        _users.GetType() == typeof(UserSet) ? _userSet.Count : 1;
    public int NumUses => _numUses;

    public TrackedValue()
    {
        _hash = StaticHash(this);
    }

    /// <summary> Registers an use of this value. </summary>
    /// <remarks> `NumUses` will be incremented regardless if this method was called with the same operand before. </remarks>
    internal override void AddUse(User user, int operIdx)
    {
        Assert(GetType() != typeof(Variable) || user is LoadVarInst or StoreVarInst or VarAddrInst);
        Assert(user.Operands[operIdx] == this);
        TryAddUse(user);
        _numUses++;
    }

    /// <summary> Deletes an use of this value. </summary>
    /// <remarks> This method will do nothing if this value is still referenced in `user.Operands`, unless `removeEvenIfStillBeingUsed = true` </remarks>
    internal override void RemoveUse(User user, int operIdx, bool removeEvenIfStillBeingUsed = false)
    {
        Assert(user.Operands[operIdx] != this || removeEvenIfStillBeingUsed);
        if (!removeEvenIfStillBeingUsed && IsBeingUsedBy(user)) return;

        if (_users == user) {
            _users = null!;
            _numUses = 0;
        } else if (_users != null) {
            _userSet.Remove(user);
            _numUses--;
        }
    }
    private bool IsBeingUsedBy(User user)
    {
        foreach (var oper in user.Operands) {
            if (oper == this) {
                return true;
            }
        }
        return false;
    }

    public User? GetFirstUser()
    {
        if (_users != null) {
            if (_users.GetType() == typeof(UserSet)) {
                foreach (var user in _userSet) {
                    return user;
                }
            }
            return _singleUser;
        }
        return null;
    }

    /// <summary> Replace uses of this value with `newValue`. Use list is cleared on return. </summary>
    public void ReplaceUses(Value newValue)
    {
        if (newValue == this || _users == null) return;

        if (newValue is TrackedValue newTrackedValue) {
            if (_users.GetType() == typeof(UserSet)) {
                foreach (var user in _userSet._slots) {
                    if (user != null) {
                        ReplaceUse(newTrackedValue, user);
                    }
                }
            } else {
                ReplaceUse(newTrackedValue, _singleUser);
            }
            //Transfer the user slots to the newValue to avoid copying.
            //ReplaceUse() already handles this.
            if (newTrackedValue._users == null) {
                newTrackedValue._users = _users;
            }
            newTrackedValue._numUses += _numUses;
        }
        _users = null!;
        _numUses = 0;
    }
    private void ReplaceUse(TrackedValue newValue, User user)
    {
        var opers = user._operands;
        for (int i = 0; i < opers.Length; i++) {
            if (opers[i] == this) {
                opers[i] = newValue;
            }
        }
        if (newValue._users != null) {
            newValue.TryAddUse(user);
        }
    }

    private void TryAddUse(User user)
    {
        if (_users == null) {
            _users = user;
        } else if (_users != user) {
            if (_users.GetType() != typeof(UserSet)) {
                InitUserSet();
            }
            _userSet.Add(user);
        }
    }
    private void InitUserSet()
    {
        var set = new UserSet();
        set.Add(_singleUser);
        _users = set;
    }

    public override int GetHashCode() => _hash;
    private static int StaticHash(object obj)
    {
        //This is a Fibonacci hash. It's very fast, compact, and generates satisfactory results.
        //The result must be cached, because it will change when the GC compacts the heap.
        ulong addr = Unsafe.As<object, ulong>(ref obj); // *&obj
        return (int)((addr * 11400714819323198485) >> 32);
    }

    /// <summary> Returns an enumerator containing the instructions using this value. </summary>
    public ValueUserEnumerator Users() => new() { _slots = GetRawUserSlots() };
    /// <summary> Returns an enumerator containing the operands using this value. </summary>
    public ValueUseEnumerator Uses() => new() { _value = this, _slots = GetRawUserSlots() };

    private object? GetRawUserSlots() => _users?.GetType() == typeof(UserSet) ? _userSet._slots : _users;
}

public struct ValueUserEnumerator
{
    internal object? _slots;
    int _index;
    public User Current { get; private set; }

    public bool MoveNext()
    {
        if (_slots == null) {
            return false;
        } else if (_slots.GetType() == typeof(User[])) {
            var arr = Unsafe.As<User[]>(_slots);
            while (_index < arr.Length) {
                Current = arr[_index++];
                if (Current != null) {
                    return true;
                }
            }
            return false;
        } else {
            Current = Unsafe.As<User>(_slots);
            return _index++ == 0;
        }
    }

    public ValueUserEnumerator GetEnumerator() => this;
}
public struct ValueUseEnumerator
{
    internal Value _value;
    internal object? _slots;
    User? _user;
    int _index, _operIndex;
    public (User Inst, int OperIdx) Current => (_user!, _operIndex);

    public bool MoveNext()
    {
        if (_slots == null) {
            return false;
        }
        while (true) {
            if (_user != null) {
                var opers = _user.Operands;
                while (++_operIndex < opers.Length) {
                    if (opers[_operIndex] == _value) {
                        return true;
                    }
                }
            }
            if (!MoveNextUser()) {
                return false;
            }
            _operIndex = -1;
        }
    }

    private bool MoveNextUser()
    {
        if (_slots!.GetType() == typeof(User[])) {
            var arr = Unsafe.As<User[]>(_slots);
            while (_index < arr.Length) {
                _user = arr[_index++];
                if (_user != null) {
                    return true;
                }
            }
            return false;
        } else {
            _user = Unsafe.As<User>(_slots);
            return _index++ == 0;
        }
    }

    public ValueUseEnumerator GetEnumerator() => this;
}