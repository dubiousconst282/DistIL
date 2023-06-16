namespace DistIL.IR.Utils.Parser;

public class ParseError
{
    public string SourceCode { get; }
    public string Message { get; }
    public AbsRange Position { get; }

    public ParseError(string srcCode, string msg, AbsRange pos)
    {
        SourceCode = srcCode;
        Message = msg;
        Position = pos;
    }

    public string GetDetailedMessage()
    {
        var (line, col) = GetLinePos();
        var contextLines = GetSourceContext(SourceCode, Position.Start, Position.End);
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
    private static string GetSourceContext(string str, int start, int end)
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