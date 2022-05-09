namespace DistIL.Util;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class BitSet : IEquatable<BitSet>
{
    private ulong[] _data;
    private int _len;

    public int Length => _len;

    public BitSet(int initialLen = 256)
    {
        Ensure(initialLen >= 0);
        _len = initialLen;
        _data = new ulong[(_len + 63) >> 6];
    }

    public bool this[int index]
    {
        get {
            return index < _len && 
                   (GetWord(index, false) & (1ul << index)) != 0;
        }
        set {
            ref ulong w = ref GetWord(index, true);
            ulong mask = 1ul << index;
            if (value) {
                w |= mask;
            } else {
                w &= ~mask;
            }
            //w = (w & ~mask) | (value ? mask : 0);
        }
    }

    /// <summary> Sets the bit at the specified position, and returns whether it was previously clear. </summary>
    public bool Add(int index)
    {
        ref ulong w = ref GetWord(index, true);
        ulong mask = 1ul << index;
        bool wasClear = (w & mask) == 0;
        w |= mask;
        return wasClear;
    }

    /// <summary> Clears the bit at the specified index, and returns whether it was previously set. </summary>
    public bool Remove(int index)
    {
        if ((uint)index >= (uint)_len) {
            return false;
        }
        ref ulong w = ref GetWord(index, false);
        ulong mask = 1ul << index;
        bool wasSet = (w & mask) != 0;
        w &= ~mask;
        return wasSet;
    }

    /// <summary> Sets the bit at the specified index. </summary>
    public void Set(int index)
    {
        GetWord(index, true) |= (1ul << index);
    }

    /// <summary> Sets the bit at the specified index. </summary>
    public bool Contains(int index)
    {
        return this[index];
    }

    /// <summary> Returns the number of set bits. </summary>
    public int Count()
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

    public void Intersect(BitSet other)
    {
        Ensure(other._len <= _len);
        var wa = _data;
        var wb = other._data;

        for (int i = 0; i < wa.Length; i++) {
            wa[i] &= wb[i];
        }
    }
    public void Union(BitSet other)
    {
        Ensure(other._len <= _len);
        var wa = _data;
        var wb = other._data;

        for (int i = 0; i < wa.Length; i++) {
            wa[i] |= wb[i];
        }
    }

    public void Clear()
    {
        Array.Clear(_data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref ulong GetWord(int bitPos, bool grow)
    {
        if ((uint)bitPos >= (uint)_len) {
            if (grow) {
                Grow(bitPos);
            } else {
                throw new ArgumentOutOfRangeException();
            }
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_data), bitPos >> 6);
    }

    private void Grow(int bitPos)
    {
        Ensure(bitPos >= 0);
        _len = bitPos + 1;

        int wordIndex = bitPos >> 6;
        if (wordIndex >= _data.Length) {
            const int GROW_AMOUNT = 512 / 64;
            Array.Resize(ref _data, wordIndex + GROW_AMOUNT);
        }
    }

    public bool Equals(BitSet? other)
    {
        return other != null &&
               _len == other._len &&
               _data.AsSpan().SequenceEqual(other._data);
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

    public Enumerator GetEnumerator() => new(_data, 0, _len);
    public Enumerator GetRangeEnumerator(int start, int end)
    {
        Ensure(
            (uint)end <= (uint)_len &&
            start >= 0 && start <= end
        );
        return new Enumerator(_data, start, end);
    }

    public struct Enumerator
    {
        ulong[] _data;
        int _index, _end;

        public int Current => _index;

        internal Enumerator(ulong[] data, int startIndex, int endIndex)
        {
            Assert(startIndex >= 0 && (endIndex >> 6) <= data.Length);
            _data = data;
            _index = startIndex - 1;
            _end = endIndex;
        }

        public bool MoveNext()
        {
            if (_index >= _end) {
                return false;
            }
            _index++;
            int wordIndex = _index >> 6;
            ulong word = _data[wordIndex] & (~0ul << _index); //mask out bits before `index % 64`

            while (true) {
                if (word != 0) {
                    _index = (wordIndex << 6) + BitOperations.TrailingZeroCount(word);
                    return _index < _end;
                }
                wordIndex++;
                if (wordIndex >= _data.Length || wordIndex > (_end >> 6)) {
                    return false;
                }
                word = _data[wordIndex];
            }
        }

        public Enumerator GetEnumerator() => this;
    }
}