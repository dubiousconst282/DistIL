namespace DistIL.Util;

internal static class StringExt
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

    public static void AppendSequence<T>(
        this StringBuilder sb, IEnumerable<T> elems,
        Action<T>? printElem = null,
        string prefix = "[", string postfix = "]", string separator = ", ")
    {
        sb.Append(prefix);
        int i = 0;
        foreach (var elem in elems) {
            if (i++ > 0) sb.Append(separator);

            if (printElem != null) {
                printElem.Invoke(elem);
            } else {
                sb.Append(elem);
            }
        }
        sb.Append(postfix);
    }

    public static (int Line, int Column) GetLinePos(ReadOnlySpan<char> text, int offset)
    {
        int ln = 1, col = 1;
        
        for (int i = 0; i < offset; i++) {
            if (text[i] == '\n') {
                ln++;
                col = 1;
            } else {
                col++;
            }
        }
        return (ln, col);
    }
}