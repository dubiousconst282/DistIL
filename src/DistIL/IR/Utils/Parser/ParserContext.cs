namespace DistIL.IR.Utils.Parser;

internal class ParserContext
{
    public string Code { get; }
    public ModuleResolver? ModuleResolver { get; }

    public ParserContext(string code, ModuleResolver? modResolver)
    {
        Code = code;
        ModuleResolver = modResolver;
    }

    public ModuleDef ResolveModule(string name)
    {
        return ModuleResolver?.Resolve(name, throwIfNotFound: false)
            ?? throw new FormatException($"Failed to resolve module '{name}'");
    }

    public Exception Error(string msg, int start, int end)
    {
        var (line, col) = GetLinePos(Code, start);
        return new FormatException($"{msg}\nat line {line}, column {col}:\n\n{GetErrorContext(Code, start, end)}");
    }
    
    public Exception Error(Node location, string msg)
    {
        //TODO: Mark node source locations
        return new FormatException(msg);
    }

    private static (int Line, int Column) GetLinePos(string str, int pos)
    {
        int ln = 1, col = 1;
        for (int i = 0; i < pos; i++) {
            if (str[i] == '\n') {
                ln++;
                col = 1;
            } else {
                col++;
            }
        }
        return (ln, col);
    }
    private static string GetErrorContext(string str, int start, int end)
    {
        const int maxLen = 32;
        int tokenLen = end - start;
        int wstart = start, wend = end; //context window pos

        while (wstart > 0 && (start - wstart) < maxLen) {
            if (str[wstart - 1] == '\n') break;
            wstart--;
        }
        while (wend < str.Length && (end - wend) < maxLen) {
            if (str[wend] == '\n') break;
            wend++;
        }
        string padding = new string(' ', start - wstart);
        string underline = new string('^', Math.Clamp(tokenLen, 1, maxLen - 1));

        return $"{str[wstart..wend]}\n{padding}{underline}{(tokenLen >= maxLen ? "..." : "")}";
    }
}