namespace DistIL.Util;

public static class StringEx
{
    public static bool EqualsIgnoreCase(this string str, string other)
    {
        return str.Equals(other, StringComparison.OrdinalIgnoreCase);
    }
    public static bool EqualsIgnoreCase(this ReadOnlySpan<char> str, ReadOnlySpan<char> other)
    {
        return str.Equals(other, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsIgnoreCase(this string str, string other)
    {
        return str.Contains(other, StringComparison.OrdinalIgnoreCase);
    }
    public static bool ContainsIgnoreCase(this ReadOnlySpan<char> str, ReadOnlySpan<char> other)
    {
        return str.Contains(other, StringComparison.OrdinalIgnoreCase);
    }

    public static void AppendSequence<T>(this StringBuilder sb, string prefix, string postfix, IReadOnlyList<T> elems, Action<T> printElem)
    {
        sb.Append(prefix);
        for (int i = 0; i < elems.Count; i++) {
            if (i > 0) sb.Append(", ");
            printElem(elems[i]);
        }
        sb.Append(postfix);
    }
}