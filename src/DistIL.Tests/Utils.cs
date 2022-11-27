namespace DistIL.Tests;

using DistIL.AsmIO;
using DistIL.IR;

class Utils
{
    public static MethodBody CreateDummyMethodBody(TypeDesc? retType = null, params TypeDesc[] paramTypes)
    {
        var pars = paramTypes
            .Select((t, i) => new ParamDef(t, "par" + i))
            .ToImmutableArray();

        var type = new TypeDef(null!, null, "DummyClass");
        var method = new MethodDef(type, retType ?? PrimType.Void, pars, "DummyMethod");
        return new MethodBody(method);
    }
}

class FakeValue : TrackedValue
{
    public int Id;
    public FakeValue(int id)
    {
        Id = id;
        ResultType = PrimType.Int32;
    }
    public override void Print(PrintContext ctx) => ctx.Print(Id.ToString());
}
