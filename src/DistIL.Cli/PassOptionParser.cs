using System.Reflection;
using System.Text.RegularExpressions;

using DistIL.Passes;

public class PassOptionParser
{
    public PassOptionParser(string str)
    {
        // OptionSet = Entry ("," OptionSet)*
        // Entry := Class "." Name "=" Value
        // Value := bool | numeric

        // --pass-opt inliner.allow-cross-assembly-calls=true
    }

    public void Populate<T>(T obj) where T : new()
    {
        var info = typeof(T).GetCustomAttribute<PassOptionsAttribute>();

        foreach (var prop in typeof(T).GetProperties()) {
           // Regex.Replace(,,)
        }
    }

    private static string CamelToSnake(string str, char separator = '_')
    {
        var buf = new char[str.Length + (str.Length / 2)];
        int j = 0;

        for (int i = 0; i < str.Length; i++) {
            if (i > 0 && char.IsUpper(str[i]) && !char.IsUpper(str[i - 1])) {
                buf[j++] = separator;
            }
            buf[j++] = char.ToLower(str[i]);
        }

        return new string(buf, 0, j);
    }
}