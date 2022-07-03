using System.Text;

using DistIL.AsmIO;
using DistIL.IR;

class Utils
{
    public static MethodBody CreateDummyMethodBody()
    {
        return new MethodBody(new MethodDef(null!, PrimType.Int32, ImmutableArray<ParamDef>.Empty, "Dummy"));
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
