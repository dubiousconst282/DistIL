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
}