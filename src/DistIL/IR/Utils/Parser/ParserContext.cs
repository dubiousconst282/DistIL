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