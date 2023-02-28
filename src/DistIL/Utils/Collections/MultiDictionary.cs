namespace DistIL.Util;

/// <summary> Represents a <see cref="Dictionary{TKey, TValue}"/> which allows multiple values per key. </summary>
public class MultiDictionary<TKey, TValue> where TKey : notnull
{
    readonly Dictionary<TKey, Bucket> _dict = new();

    public int KeyCount => _dict.Count;

    public void Add(TKey key, TValue value)
    {
        ref var bucket = ref _dict.GetOrAddRef(key, out bool exists);

        if (!exists || bucket._count >= bucket._items.Length) {
            Array.Resize(ref bucket._items, exists ? bucket._count * 2 : 4);
        }
        bucket._items[bucket._count++] = value;
    }

    public Span<TValue> Get(TKey key)
        => _dict.TryGetValue(key, out var slot) ? slot.AsSpan() : default;

    public void Clear() => _dict.Clear();

    public Dictionary<TKey, Bucket>.Enumerator GetEnumerator() => _dict.GetEnumerator();

    public struct Bucket
    {
        internal TValue[] _items;
        internal int _count;

        public int Count => _count;

        public Span<TValue> AsSpan() => _items.AsSpan(0, _count);
        public Span<TValue>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();
    }
}