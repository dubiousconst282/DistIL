namespace DistIL.Tests.IR;

using DistIL.AsmIO;
using DistIL.Frontend;
using DistIL.IR;

[Collection("ModuleResolver")]
public class MatchingTests
{
    private readonly ModuleResolver _modResolver;
    private MethodDesc _stub;


    public MatchingTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
        var type = _modResolver.Import(typeof(MatchingTests));
        _stub = type.FindMethod("StubMethod");
        
    }

    public static void StubMethod()
    {
        var x = 2 + 6;
        System.Console.WriteLine(x);
    }

    [Fact]
    public void TestMatch()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        BinaryInst? instr = null;
        Assert.True(inst.Match($"(add 42 {instr})"));
        Assert.IsType<BinaryInst>(instr);
        Assert.Equal(BinaryOp.Mul, instr.Op);

        ConstInt? x = null;
        Assert.True(inst.Match($"(add {x} (mul _ _))"));
        Assert.IsType<ConstInt>(x);
        Assert.Equal(42L, x.Value);
    }

    [Fact]
    public void TestNot()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match($"(add _ !42)"));
    }

    [Fact]
    public void TestTypedArgument()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match($"(add :int !42)"));
    }

    [Fact]
    public void Test_Strings()
    {
        var instr = new BinaryInst(BinaryOp.Add, ConstString.Create("hello"), ConstString.Create("world")); //Todo: fix

        Assert.True(instr.Match($"(add 'hello' _)"));
        Assert.True(instr.Match($"(add *'o' _)"));
        Assert.True(instr.Match($"(add 'h'* _)"));
        Assert.True(instr.Match($"(add *'l'* _)"));
    }

}