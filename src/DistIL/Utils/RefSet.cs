namespace DistIL.Util;

using System.Runtime.CompilerServices;

/// <summary> Implements a compact unordered set of object references. </summary>
/// <remarks> Enumerators will be invalidated and throw after mutations (adds/removes). </remarks>
public class RefSet<T, H>
    where T : class
    where H : struct, Hasher<T>
{
    T?[] _slots = new T[16];
    int _count;
    //Used to invalidate the enumerator when the set is changed.
    //Add()/Remove() will set it to true, and GetEnumerator() to false.
    //This approach could fail if GetEnumerator() is called on multiple threads, but we don't care about that.
    bool _changed;

    public int Count => _count;

    //JIT can't inline static interface methods atm, so that's why we're doing it this way.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(T obj)
    {
        Unsafe.SkipInit(out H h);
        return h.Hash(obj);
    }

    public bool Add(T value)
    {
        var slots = _slots;
        for (int index = Hash(value); ; index++) {
            index &= (slots.Length - 1);
            var slot = slots[index];

            if (slot == null) {
                MemUtils.WriteInvariant(slots, index, value);
                _count++;
                //Expand when load factor reaches 3/4
                //Casting to uint avoids extra sign calcs in the resulting asm.
                if ((uint)_count >= (uint)slots.Length * 3 / 4) {
                    Expand();
                }
                _changed = true;
                return true;
            }
            if (slot == value) {
                return false;
            }
        }
    }

    public bool Contains(T value)
    {
        var slots = _slots;
        for (int index = Hash(value); ; index++) {
            index &= (slots.Length - 1);
            var slot = slots[index];

            if (slot == value) {
                return true;
            }
            if (slot == null) {
                return false;
            }
        }
    }

    public bool Remove(T value)
    {
        var slots = _slots;
        for (int index = Hash(value); ; index++) {
            index &= (slots.Length - 1);
            var slot = slots[index];

            if (slot == value) {
                RemoveEntryAndShiftCluster(index);
                _count--;
                _changed = true;
                return true;
            }
            if (slot == null) {
                return false;
            }
        }
    }

    private void RemoveEntryAndShiftCluster(int i)
    {
        //https://en.wikipedia.org/wiki/Open_addressing
        var slots = _slots;
        int j = i;
        while (true) {
            slots[i] = null;

            while (true) {
                j = (j + 1) & (slots.Length - 1);
                if (slots[j] == null) return;

                int k = Hash(slots[j]!) & (slots.Length - 1);
                //determine if k lies cyclically outside (i,j]
                // |    i.k.j |
                // |....j i.k.| or  |.k..j i...|
                if (i <= j ? (i >= k || k > j) : (i >= k && k > j)) break;
            }
            MemUtils.WriteInvariant(slots, i, slots[j]);
            i = j;
        }
    }

    public void Clear()
    {
        Array.Clear(_slots);
        _count = 0;
    }

    private void Expand()
    {
        var oldSlots = _slots;
        var newSlots = _slots = new T[oldSlots.Length * 2];

        foreach (var value in oldSlots) {
            if (value == null) continue;

            for (int index = Hash(value); ; index++) {
                index &= (newSlots.Length - 1);
                if (newSlots[index] == null) {
                    MemUtils.WriteInvariant(newSlots, index, value);
                    break;
                }
                Assert(newSlots[index] != value); //slots shouldn't have dupes
            }
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        T?[] _slots;
        int _index;
        RefSet<T, H> _owner;

        public T Current { get; private set; } = null!;

        internal Enumerator(RefSet<T, H> owner) 
            => (_slots, _owner, owner._changed) = (owner._slots, owner, false);

        public bool MoveNext()
        {
            Ensure(!_owner._changed, "RefSet cannot be modified during enumeration");

            while (_index < _slots.Length) {
                Current = _slots[_index++]!;
                if (Current != null) {
                    return true;
                }
            }
            return false;
        }
    }
}
/// <inheritdoc/>
public class RefSet<T> : RefSet<T, IdentityHasher>
    where T : class
{
}

/// <inheritdoc/>
public class ValueSet<T> : RefSet<T, IRValueHasher>
    where T : IR.TrackedValue
{
}

public interface Hasher<in T>
{
    int Hash(T obj);
}

public struct IdentityHasher : Hasher<object>
{
    public int Hash(object obj) => RuntimeHelpers.GetHashCode(obj);
}
public struct IRValueHasher : Hasher<IR.TrackedValue>
{
    public int Hash(IR.TrackedValue obj) => obj._hash;
}