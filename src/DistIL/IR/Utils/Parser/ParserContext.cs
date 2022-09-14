namespace DistIL.IR.Utils.Parser;

public class ParserContext
{
    public string SourceCode { get; }
    public ModuleResolver ModuleResolver { get; }
    public List<ParseError> Errors { get; }

    public ParserContext(string code, ModuleResolver modResolver)
    {
        SourceCode = code;
        ModuleResolver = modResolver;
        Errors = new();
    }

    public ModuleDef ResolveModule(string name)
    {
        return ModuleResolver?.Resolve(name, throwIfNotFound: false)
            ?? throw new FormatException($"Failed to resolve module '{name}'");
    }

    internal void Error(string msg, int start, int end)
    {
        Errors.Add(new ParseError(SourceCode, msg, start, end));
        
        if (Errors.Count > 100) {
            throw new FormatException("Halting parsing due to error limit");
        }
    }
    internal Exception Fatal(string msg, int start, int end)
    {
        var error = new ParseError(SourceCode, msg, start, end);
        return new FormatException(error.GetDetailedMessage());
    }
    
    internal Exception Error(Node location, string msg)
    {
        //TODO: Mark node source locations
        return new FormatException(msg);
    }
}
public struct ParseError
{
    public string SourceCode { get; }
    public string Message { get; }
    public (int Start, int End) Position { get; }

    public ParseError(string srcCode, string msg, int start, int end)
    {
        SourceCode = srcCode;
        Message = msg;
        Position = (start, end);
    }

    public string GetDetailedMessage()
    {
        var (line, col) = GetLinePos();
        var contextLines = GetErrorContext(SourceCode, Position.Start, Position.End);
        return $"{Message}\non line {line}, column {col}:\n\n{contextLines}";
    }

    public (int Line, int Column) GetLinePos()
    {
        int ln = 1, col = 1;
        for (int i = 0; i < Position.Start; i++) {
            if (SourceCode[i] == '\n') {
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
        const int kMaxLen = 60;
        int tokenLen = end - start;
        int wstart = start, wend = end; //context window pos

        while (wstart > 0 && (start - wstart) < kMaxLen) {
            if (str[wstart - 1] == '\n') break;
            wstart--;
        }
        while (wend < str.Length && (end - wend) < kMaxLen) {
            if (str[wend] == '\n') break;
            wend++;
        }
        string padding = new(' ', start - wstart);
        string underline = new('^', Math.Clamp(tokenLen, 1, kMaxLen - 1));

        return $"{str[wstart..wend]}\n{padding}{underline}{(tokenLen >= kMaxLen ? "..." : "")}";
    }
}