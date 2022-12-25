namespace DistIL.IR.Utils.Parser;

using MethodAttribs = System.Reflection.MethodAttributes;

public class ParserContext
{
    public string SourceCode { get; }
    public ModuleResolver ModuleResolver { get; }

    public List<ParseError> Errors { get; } = new();
    public List<MethodBody> DeclaredMethods { get; } = new();

    public bool HasErrors => Errors.Count > 0;

    public ParserContext(string code, ModuleResolver modResolver)
    {
        SourceCode = code;
        ModuleResolver = modResolver;
    }

    public ModuleDef ImportModule(string name)
    {
        return ModuleResolver?.Resolve(name, throwIfNotFound: false)
            ?? throw new FormatException($"Failed to resolve module '{name}'");
    }

    public virtual MethodBody DeclareMethod(
        TypeDef parentType, string name,
        TypeSig returnSig, ImmutableArray<ParamDef> paramSig,
        ImmutableArray<GenericParamType> genParams, MethodAttribs attribs)
    {
        var def = parentType.CreateMethod(name, returnSig, paramSig, attribs, genParams);
        var body = def.Body = new MethodBody(def);
        DeclaredMethods.Add(body);
        return body;
    }

    internal void Error(string msg, AbsRange pos)
    {
        Errors.Add(new ParseError(SourceCode, msg, pos));
        
        if (Errors.Count > 100) {
            throw Fatal("Halting parsing due to error limit", pos);
        }
    }
    internal Exception Fatal(string msg, AbsRange pos)
    {
        var error = new ParseError(SourceCode, msg, pos);
        Errors.Add(error);
        return new FormatException(error.GetDetailedMessage());
    }
}
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