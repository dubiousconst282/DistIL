namespace DistIL.Util;

using DistIL.IR;

/// <summary>
/// A compact hash set where keys are <see cref="TrackedValue"/> object references.
/// 
/// The implementation uses a linear probing hash set, with load threshold of `0.75`.
/// Insertion order is not preserved.
/// </summary>
public class ValueSet<TValue> where TValue : TrackedValue
{
    internal TValue?[] _slots = new TValue[4];
    internal int _count;

    public int Count => _count;

    public bool Add(TValue value)
    {
        var slots = _slots;
        for (int hash = Hash(value); ; hash++) {
            int index = hash & (slots.Length - 1);
            var slot = slots[index];

            if (slot == value) return false;
            if (slot == null) {
                slots[index] = value;
                //expand when load factor reaches 3/4 (0.75)
                if (++_count >= slots.Length * 3 / 4) {
                    Expand();
                }
                return true;
            }
        }
    }

    public bool Contains(TValue value)
    {
        var slots = _slots;
        for (int hash = Hash(value); ; hash++) {
            int index = hash & (slots.Length - 1);
            var slot = slots[index];

            if (slot == value) {
                return true;
            }
            if (slot == null) {
                return false;
            }
        }
    }

    public bool Remove(TValue value)
    {
        var slots = _slots;
        for (int hash = Hash(value); ; hash++) {
            int index = hash & (slots.Length - 1);
            var slot = slots[index];

            if (slot == value) {
                RemoveEntryAndShiftCluster(index);
                _count--;
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
            slots[i] = slots[j];
            i = j;
        }
    }

    public void Clear()
    {
        _slots = new TValue[4];
        _count = 0;
    }

    private void Expand()
    {
        var oldSlots = _slots;
        var newSlots = new TValue[oldSlots.Length * 2];
        _slots = newSlots;

        foreach (var value in oldSlots) {
            if (value == null) continue;

            for (int hash = Hash(value); ; hash++) {
                int index = hash & (newSlots.Length - 1);
                if (newSlots[index] == null) {
                    newSlots[index] = value;
                    break;
                }
                Assert(newSlots[index] != value); //slots can't have dupes
            }
        }
    }

    private static int Hash(TValue user) => user._hash;

    public Enumerator GetEnumerator() => new() { _slots = _slots };

    public struct Enumerator
    {
        internal TValue?[] _slots;
        int _index;

        public TValue Current { get; private set; }

        public bool MoveNext()
        {
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