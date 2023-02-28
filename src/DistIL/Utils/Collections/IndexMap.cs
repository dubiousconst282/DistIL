namespace DistIL.Util;

/// <summary> Bi-directional map of <typeparamref name="T"/> and sequential integer ids. </summary>
public class IndexMap<T> where T : notnull
{
    internal readonly Dictionary<T, int> _ids;
    internal T[]? _items;

    public IndexMap(IEqualityComparer<T>? comparer = null)
        => _ids = new(comparer);

    /// <summary> Gets or adds a index for the given value. </summary>
    public int Add(T value)
    {
        ref int id = ref _ids.GetOrAddRef(value, out bool exists);
        if (!exists) {
            id = _ids.Count - 1;
        }
        return id;
    }

    /// <summary> Gets the index for the given value, or <c>-1</c> if it does not exist. </summary>
    public int IndexOf(T value) => _ids.GetValueOrDefault(value, -1);

    /// <summary> Gets the value at the given index. </summary>
    public T At(int id)
    {
        if (_items == null || _items.Length != _ids.Count) {
            _items = _ids.Keys.ToArray();
        }
        return _items[id];
    }

    public bool Contains(T value) => _ids.ContainsKey(value);

    public void Clear()
    {
        _ids.Clear();
        _items = null;
    }
}