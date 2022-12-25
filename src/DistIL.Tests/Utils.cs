namespace DistIL.Tests;

using DistIL.AsmIO;
using DistIL.IR;

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
        string? name = null)
    {
        var type = new TypeDef(null!, null, "DummyClass");
        var method = new MethodDef(type, retType, paramSig, name ?? "DummyMethod", attribs);
        return new MethodBody(method);
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
