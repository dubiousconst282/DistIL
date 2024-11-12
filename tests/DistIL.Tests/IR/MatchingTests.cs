namespace DistIL.Tests.IR;

using DistIL.AsmIO;
<<<<<<< HEAD
using DistIL.Frontend;
=======
>>>>>>> 6ecbd15f521745917994d1f0e542d3386c88a231
using DistIL.IR;

[Collection("ModuleResolver")]
public class MatchingTests
{
    private readonly ModuleResolver _modResolver;
<<<<<<< HEAD
    private MethodDesc _stub;

=======
    private readonly MethodDesc? _stub;
>>>>>>> 6ecbd15f521745917994d1f0e542d3386c88a231

    public MatchingTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
<<<<<<< HEAD
        var type = _modResolver.Import(typeof(MatchingTests));
        _stub = type.FindMethod("StubMethod");
        
    }

    public static void StubMethod()
    {
        var x = 2 + 6;
        System.Console.WriteLine(x);
=======
        _stub = _modResolver.Import(typeof(MatchingTests)).FindMethod("Stub");
    }

    static void Stub(string str, string s)
    {

>>>>>>> 6ecbd15f521745917994d1f0e542d3386c88a231
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
    public void TestNumberOperator()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match($"(add >5 _)"));
    }

    [Fact]
    public void Test_Strings()
    {
        var instr = new CallInst(_stub, [ConstString.Create("hello"), ConstString.Create("world")]);

        Assert.True(instr.Match($"(call 'hello' _)"));
        Assert.True(instr.Match($"(call *'o' _)"));
        Assert.True(instr.Match($"(call _ 'h'*)"));
        Assert.True(instr.Match($"(call *'l'* _)"));
    }

}