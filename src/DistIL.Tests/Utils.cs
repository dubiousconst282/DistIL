using System.Text;

using DistIL.AsmIO;
using DistIL.IR;

class Utils
{
    public static MethodBody CreateDummyMethodBody(TypeDesc? retType = null, params TypeDesc[] paramTypes)
    {
        var pars = paramTypes
            .Select((t, i) => new ParamDef(t, i))
            .ToImmutableArray();
        var def = new MethodDef(null!, retType ?? PrimType.Void, pars, "Dummy");
        return new MethodBody(def);
    }
}

class DummyValue : TrackedValue
{
    public int Id;
    public DummyValue(int id)
    {
        Id = id;
        ResultType = PrimType.Int32;
    }
    public override void Print(PrintContext ctx) => ctx.Print(Id.ToString());
}
