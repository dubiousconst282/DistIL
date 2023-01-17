namespace DistIL.Tests;

using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.IR.Utils.Parser;

class Utils
{
    public static MethodBody CreateDummyMethodBody(TypeDesc? retType = null, params TypeDesc[] paramTypes)
    {
        return CreateDummyMethodBody(
            retType ?? PrimType.Void,
            paramTypes
                .Select((t, i) => new ParamDef(t, "par" + i))
                .ToImmutableArray());
    }
    public static MethodBody CreateDummyMethodBody(
        TypeDesc retType, ImmutableArray<ParamDef> paramSig, 
        System.Reflection.MethodAttributes attribs = default,
        string? name = null, string? typeName = null)
    {
        var type = new TypeDef(null!, null, typeName + "DummyClass");
        var method = new MethodDef(type, retType, paramSig, name ?? "DummyMethod", attribs);
        return new MethodBody(method);
    }

    public static Dictionary<string, MethodBody> ParseMethodDecls(string filename, ModuleResolver resolver)
    {
        var source = File.ReadAllText(filename);
        var ctx = new FakeParserContext(source, resolver);
        new IRParser(ctx).ParseUnit();

        return ctx.DeclaredMethods.ToDictionary(e => e.Definition.Name);
    }
}

class FakeTrackedValue : TrackedValue
{
    public int Id;
    public FakeTrackedValue(int id)
    {
        Id = id;
        ResultType = PrimType.Int32;
    }
    public override void Print(PrintContext ctx) => ctx.Print(Id.ToString());
}

class FakeParserContext : ParserContext
{
    public FakeParserContext(string code, ModuleResolver modResolver)
        : base(code, modResolver) { }

    public override MethodBody DeclareMethod(
        TypeDef parentType, string name,
        TypeSig returnSig, ImmutableArray<ParamDef> paramSig,
        ImmutableArray<GenericParamType> genParams, System.Reflection.MethodAttributes attribs)
    {
        var body = Utils.CreateDummyMethodBody(returnSig.Type, paramSig, attribs, name, parentType.Name);
        DeclaredMethods.Add(body);
        return body;
    }
}