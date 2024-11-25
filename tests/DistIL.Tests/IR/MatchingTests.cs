namespace DistIL.Tests.IR;

using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;

[Collection("ModuleResolver")]
public class MatchingTests
{
    private readonly ModuleResolver _modResolver;
    private MethodDesc _stub;
    private readonly ModuleDef _module;
    private readonly TypeDef _testType;


    public MatchingTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
        var type = _modResolver.Import(typeof(MatchingTests));
        _stub = type.FindMethod("StubMethod");
        _module = _modResolver.Create("Test");
        _testType = _module.CreateType("Test", "Stub");
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

        Assert.True(inst.Match("(add 42 {instr})", out var outputs));
        var instr = (BinaryInst)outputs["instr"];
        Assert.IsType<BinaryInst>(instr);
        Assert.Equal(BinaryOp.Mul, instr.Op);

        Assert.True(inst.Match("(add {x} (mul _ _))", out outputs));
        var x = (ConstInt)outputs["x"];
        Assert.IsType<ConstInt>(x);
        Assert.Equal(42L, x.Value);
    }

    [Fact]
    public void TestSubMatchOutput()
    {
        var inst = new BinaryInst(BinaryOp.Sub, new BinaryInst(BinaryOp.Add, ConstInt.CreateI(1), ConstInt.CreateI(3)), ConstInt.CreateI(3));

        Assert.True(inst.Match("(sub {lhs:(add {x} $y)} $y)", out var outputs));
        var instr = (BinaryInst)outputs["lhs"];
        Assert.IsType<BinaryInst>(instr);
        Assert.Equal(BinaryOp.Add, instr.Op);
    }

    [Fact]
    public void TestReplace()
    {
        var method = _testType.CreateMethod("ReplaceMe", new TypeSig(PrimType.Void), []);
        var body = new MethodBody(method);
        var builder = new IRBuilder(body.CreateBlock());

        var inst = new BinaryInst(BinaryOp.Sub, new BinaryInst(BinaryOp.Add, ConstInt.CreateI(1), ConstInt.CreateI(3)), ConstInt.CreateI(3));
        builder.Emit(inst);

        inst.Replace("(sub {lhs:(add $x $y)} $y) -> $x");
    }

    [Fact]
    public void TestNot()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match("(add _ !42)"));
    }

    [Fact]
    public void TestReturn()
    {
        var inst = new ReturnInst(new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match("(ret _)"));
    }

    [Fact]
    public void TestCompare()
    {
        var inst = new CompareInst(CompareOp.Eq, ConstInt.CreateI(1), ConstInt.CreateI(3));

        Assert.True(inst.Match("(cmp.eq)"));
    }

    [Fact]
    public void TestUnary()
    {
        var inst = new UnaryInst(UnaryOp.Neg, new UnaryInst(UnaryOp.Neg, ConstInt.CreateI(2)));

        Assert.True(inst.Match("(neg (neg {x}))"));
    }

    [Fact]
    public void TestTypedArgument()
    {
        var inst = new BinaryInst(BinaryOp.Add, ConstInt.CreateI(42), new BinaryInst(BinaryOp.Mul, ConstInt.CreateI(1), ConstInt.CreateI(3)));

        Assert.True(inst.Match("(add #int !42)"));
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