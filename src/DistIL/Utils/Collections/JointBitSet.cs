namespace DistIL.Util;

/// <summary> Ordered set of arbitrary items backed by a <see cref="BitSet"/>, which indexes a shared <see cref="IndexMap{T}"/>. </summary>
public struct JointBitSet<T> where T : notnull
{
    public readonly IndexMap<T> Palette;
    public readonly BitSet Entries;

    public JointBitSet(IndexMap<T> palette, BitSet entries)
        => (Palette, Entries) = (palette, entries);

    public bool Contains(T value)
        => Entries.Contains(Palette.IndexOf(value)); // BitSet.Contains() allows negative indices

    public bool Add(T value)
        => Entries.Add(Palette.Add(value));

    public bool Remove(T value)
        => Entries.Remove(Palette.IndexOf(value));

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var item in this) {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(item);
        }
        return sb.ToString();
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (int id in Entries) {
            yield return Palette.At(id);
        }
    }
}