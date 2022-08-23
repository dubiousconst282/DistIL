namespace DistIL.Util;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class BitSet : IEquatable<BitSet>
{
    private ulong[] _data;

    public ulong[] RawData => _data;

    public BitSet(int initialCapacity = 256)
    {
        _data = new ulong[(initialCapacity + 63) >> 6];
    }

    public bool this[int index] {
        get => Contains(index);
        set {
            ref ulong w = ref GetOrAddWord(index);
            ulong mask = 1ul << index;
            if (value) {
                w |= mask;
            } else {
                w &= ~mask;
            }
            //w = (w & ~mask) | (value ? mask : 0);
        }
    }

    /// <summary> Sets the bit at the specified index, and returns whether it was previously clear. </summary>
    public bool Add(int index)
    {
        ref ulong w = ref GetOrAddWord(index);
        ulong prev = w;
        w |= (1ul << index);
        return w != prev;
    }

    /// <summary> Clears the bit at the specified index, and returns whether it was previously set. </summary>
    public bool Remove(int index)
    {
        if (!IsIndexValid(index)) {
            return false;
        }
        ref ulong w = ref GetWord(index);
        ulong prev = w;
        w &= ~(1ul << index);
        return w != prev;
    }

    /// <summary> Checks if the bit at the specified index is set. </summary>
    public bool Contains(int index)
    {
        return IsIndexValid(index) &&
               ((GetWord(index) >> index) & 1) != 0;
    }

    /// <summary> Computes the number of set bits. </summary>
    public int PopCount()
    {
        int r = 0;
        foreach (ulong w in _data) {
            r += BitOperations.PopCount(w);
        }
        return r;
    }

    /// <summary> Checks if any bit is set between start (inclusive) and end (exclusive) </summary>
    public bool ContainsRange(int start, int end)
    {
        return GetRangeEnumerator(start, end).MoveNext();
    }

    /// <summary> Removes the elements of the specified set from this set, and returns whether any change occurred. </summary>
    public bool Intersect(BitSet other)
    {
        var wa = _data;
        var wb = other._data;
        int count = Math.Min(wb.Length, wa.Length);
        ulong changed = 0;

        for (int i = 0; i < count; i++) {
            ulong prev = wa[i];
            ulong next = prev & wb[i];
            wa[i] = next;
            changed |= prev ^ next;
        }
        return changed != 0;
    }

    /// <summary> Adds the elements of the specified set to this set, and returns whether any change occurred. </summary>
    public bool Union(BitSet other)
    {
        var wa = _data;
        var wb = other._data;
        ulong changed = 0;

        if (wb.Length > wa.Length) {
            //Note: passing wa by ref will prevent the JIT from enregistering it.
            Array.Resize(ref _data, wb.Length);
            wa = _data;
        }
        for (int i = 0; i < wb.Length; i++) {
            ulong prev = wa[i];
            ulong next = prev | wb[i];
            wa[i] = next;
            changed |= prev ^ next;
        }
        return changed != 0;
    }

    /// <summary> Adds the differences between `a` and `b` (a ∩ b') to this set, and returns whether any change occurred. </summary>
    public bool UnionDiffs(BitSet a, BitSet b)
    {
        var dst = _data;
        var wa = a._data;
        var wb = b._data;
        ulong changed = 0;

        if (wa.Length > dst.Length) {
            //Note: passing dst by ref will prevent the JIT from enregistering it.
            Array.Resize(ref _data, wa.Length);
            dst = _data;
        }
        for (int i = 0; i < wa.Length; i++) {
            ulong prev = dst[i];
            ulong next = prev | (wa[i] & ~wb[i]);
            dst[i] = next;
            changed |= prev ^ next;
        }
        return changed != 0;
    }

    public void Clear()
    {
        _data.AsSpan().Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong GetWord(int bitPos)
    {
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_data), bitPos >> 6);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong GetOrAddWord(int bitPos)
    {
        if (!IsIndexValid(bitPos)) {
            Grow(bitPos);
        }
        return ref GetWord(bitPos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsIndexValid(int bitPos) => (uint)(bitPos >> 6) < (uint)_data.Length;

    private void Grow(int bitPos)
    {
        if (bitPos < 0) {
            throw new IndexOutOfRangeException();
        }
        int newSize = Math.Max(bitPos / 64 + 8, (int)(_data.Length * 6L / 4L));
        Array.Resize(ref _data, newSize);
    }

    public bool Equals(BitSet? other)
    {
        return other != null && _data.AsSpan().SequenceEqual(other._data);
    }
    public override bool Equals(object? obj)
    {
        return obj is BitSet other && Equals(other);
    }
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(MemoryMarshal.AsBytes(_data.AsSpan()));
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        foreach (int index in this) {
            if (sb.Length > 1) sb.Append(", ");
            if (sb.Length > 1024) { sb.Append("..."); break; }

            sb.Append(index);
        }
        sb.Append("]");
        return sb.ToString();
    }

    public Enumerator GetEnumerator() => new(_data, 0, _data.Length * 64);
    public Enumerator GetRangeEnumerator(int start, int end)
    {
        Ensure((uint)start <= (uint)end);
        return new Enumerator(_data, start, Math.Min(end, _data.Length * 64));
    }

    public struct Enumerator
    {
        ulong _word;
        int _basePos, _end;
        ulong[] _data;

        public int Current { get; private set; } = 0;

        internal Enumerator(ulong[] data, int start, int end)
        {
            Assert(start >= 0 && (end >> 6) <= data.Length);
            _data = data;
            _end = end;

            _word = (data[start >> 6] >> start) << start; //clear bits before `start % 64`
            _basePos = start & ~63; //floor(start / 64) * 64
        }

        public bool MoveNext()
        {
            while (true) {
                if (_word != 0) {
                    Current = _basePos + BitOperations.TrailingZeroCount(_word);
                    _word &= _word - 1; //clear lsb
                    return Current < _end;
                }
                _basePos += 64;
                if (_basePos >= _end) {
                    return false;
                }
                _word = _data[_basePos >> 6];
            }
        }

        public Enumerator GetEnumerator() => this;
    }
}