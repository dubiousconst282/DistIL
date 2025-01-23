namespace DistIL.Util;

using System.Numerics;
using System.Runtime.CompilerServices;

/// <summary> Implements a compact unordered set of object references. </summary>
/// <remarks> Enumerators will be invalidated and throw after mutations (adds/removes). </remarks>
public class RefSet<T> where T : class
{
    struct Slot { public T? value; }
    Slot[] _slots;
    int _count;
    // Used to invalidate the enumerator when the set is changed.
    // Add()/Remove() will set it to true, and GetEnumerator() to false.
    // This approach could fail if GetEnumerator() is called on multiple threads, but we don't care about that.
    bool _changed;

    public int Count => _count;

    public RefSet()
    {
        _slots = new Slot[8];
    }
    public RefSet(int initialCapacity)
    {
        // The initial capacity cannot be less than the load factor
        _slots = new Slot[Math.Max(4, BitOperations.RoundUpToPowerOf2((uint)initialCapacity))];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(T obj) => RuntimeHelpers.GetHashCode(obj);

    public bool Add(T value)
    {
        var slots = _slots;
        for (int index = Hash(value); ; index++) {
            index &= (slots.Length - 1);
            var slot = slots[index].value;

            if (slot == null) {
                slots[index].value = value;
                _count++;
                // Expand when load factor reaches 3/4
                // Casting to uint avoids extra sign calcs in the resulting asm.
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
            var slot = slots[index].value;

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
            var slot = slots[index].value;

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
        // https://en.wikipedia.org/wiki/Open_addressing
        var slots = _slots;
        int j = i;
        while (true) {
            slots[i].value = null;

            while (true) {
                j = (j + 1) & (slots.Length - 1);
                if (slots[j].value == null) return;

                int k = Hash(slots[j].value!) & (slots.Length - 1);
                // determine if k lies cyclically outside (i,j]
                // |    i.k.j |
                // |....j i.k.| or  |.k..j i...|
                if (i <= j ? (i >= k || k > j) : (i >= k && k > j)) break;
            }
            slots[i] = slots[j];
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
        var newSlots = _slots = new Slot[oldSlots.Length * 2];

        foreach (var slot in oldSlots) {
            if (slot.value == null) continue;

            for (int index = Hash(slot.value); ; index++) {
                index &= (newSlots.Length - 1);
                if (newSlots[index].value == null) {
                    newSlots[index] = slot;
                    break;
                }
                Debug.Assert(newSlots[index].value != slot.value); // slots shouldn't have dupes
            }
        }
    }

    public Iterator GetEnumerator() => new(this);

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        int i = 0;
        foreach (var elem in this) {
            if (sb.Length > 1024) {
                sb.Append(", ...");
                break;
            } else if (i++ > 0) {
                sb.Append(", ");
            }
            sb.Append(elem);
        }
        return sb.Append("]").ToString();
    }

    public struct Iterator : Iterator<T>
    {
        Slot[] _slots;
        int _index;
        RefSet<T> _owner;

        public T Current { get; private set; } = null!;

        internal Iterator(RefSet<T> owner) 
            => (_slots, _owner, owner._changed) = (owner._slots, owner, false);

        public bool MoveNext()
        {
            Ensure.That(!_owner._changed, "RefSet cannot be modified during enumeration");

            while (_index < _slots.Length) {
                Current = _slots[_index++].value!;
                if (Current != null) {
                    return true;
                }
            }
            return false;
        }
    }
}