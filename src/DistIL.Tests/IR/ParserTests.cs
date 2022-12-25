namespace DistIL.Tests.IR;

using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.IR.Utils.Parser;
using DistIL.Util;

[Collection("ModuleResolver")]
public class ParserTests
{
    readonly ModuleResolver _modResolver;
    readonly ModuleDef _corelib;

    public ParserTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
        _corelib = _modResolver.CoreLib;
    }

    [Fact]
    internal void ParseType()
    {
        var t_List = _corelib.FindType("System.Collections.Generic", "List`1", throwIfNotFound: true);

        Check("System.Int32", _modResolver.SysTypes.Int32);
        Check("int[]", PrimType.Int32.CreateArray());
        Check("int[][]", PrimType.Int32.CreateArray().CreateArray());
        Check("int*[]&", PrimType.Int32.CreatePointer().CreateArray().CreateByref());
        Check(
            "System.Collections.Generic.List`1+Enumerator[string[]]&",

            t_List.GetNestedType("Enumerator").GetSpec(
                ImmutableArray.Create<TypeDesc>(PrimType.String.CreateArray())
            ).CreateByref()
        );

        void Check(string str, TypeDesc expType)
        {
            var actType = CreateParser(str).ParseType();
            Assert.Equal(expType, actType);
        }
    }

    [Fact]
    internal void ParseFullProgram()
    {
        string code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::M1(int #arg1, int #arg2) {
Block1:
    int tmp = add.ovf #arg1, 4
    bool cond = icmp.slt tmp, #arg2
    goto cond ? Block2 : Block3
Block2: goto Block3
Block3:
    int tmp2 = phi [Block1 -> tmp], [Block2 -> -123]
    int tmp3 = call Math::Abs(int: tmp2)
    ret tmp3
}

static float ParserDummy::M2(float #x) {
Entry:
    //x^2*(3-2*x)
    float xsq = fmul #x, #x
    float t1 = fmul 2.0f, #x
    float t2 = fsub 3.0f, t1
    float t3 = fmul xsq, t2
    ret t3
}
";
        var parser = CreateParser(code);
        parser.ParseUnit();

        var decls = parser.Context.DeclaredMethods.ToDictionary(m => m.Definition.Name);
        var body1 = decls["M1"];

        Assert.Equal(3, body1.NumBlocks);
        var insts = body1.Instructions().ToArray();

        Assert.True(insts[0] is BinaryInst { Op: BinaryOp.AddOvf, Left: Argument { Index: 0 }, Right: ConstInt { Value: 4 } });
        Assert.True(insts[1] is CompareInst { Op: CompareOp.Slt, Left: var cmpL, Right: Argument { Index: 1 } } && cmpL == insts[0]);
        Assert.True(insts[2] is BranchInst br1 && br1.Cond == insts[1]);
        Assert.True(insts[3] is BranchInst { IsJump: true });
        Assert.True(insts[4] is PhiInst phi &&
            phi.GetBlock(0) == insts[0].Block &&
            phi.GetValue(0) == insts[0] &&
            phi.GetBlock(1) == insts[3].Block &&
            phi.GetValue(1) is ConstInt { Value: -123 }
        );
        Assert.True(insts[5] is CallInst { Method.Name: "Abs", Args: [var callArg0] } && callArg0 == insts[4]);
        Assert.True(insts[6] is ReturnInst ret && ret.Value == insts[5]);

        var body2 = decls["M2"];
        Assert.Equal(1, body2.NumBlocks);

        var expr = body2.EntryBlock.Last;
        Assert.True(expr is ReturnInst {
            Value: BinaryInst { 
                Op: BinaryOp.FMul,
                Left: BinaryInst {
                    Op: BinaryOp.FMul, Left: Argument, Right: Argument
                },
                Right: BinaryInst {
                    Op: BinaryOp.FSub,
                    Left: ConstFloat { Value: 3.0f },
                    Right: BinaryInst {
                        Left: ConstFloat { Value: 2.0f },
                        Right: Argument
                    }
                }
            }
        });
    }

    [Fact]
    public void ParseUnseenIdentifiers()
    {
        string code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::TestCase() {
Entry:
    goto Head
Body:
    int b = mul a, 1
    bool c = icmp.slt b, 20
    ret
Head:
    int a = add 1, 4
    goto Body
}
";
        var body = Parse(code);

        var block = body.EntryBlock.Succs.First().Succs.First();
        var insts = block.NonPhis().ToArray();

        Assert.True(insts[0] is BinaryInst { Op: BinaryOp.Mul, Left: BinaryInst, Right: ConstInt });
        Assert.True(insts[1] is CompareInst { Op: CompareOp.Slt, Left: var cmpL, Right: ConstInt } && cmpL == insts[0]);
    }

    [Fact]
    public void ParseVarDecls()
    {
            string code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::TestCase(int[] arr) {
$:
    int a, b
    String c
    int[]^ pin
Entry:
    int& addr = varaddr $b
    stvar $pin, #arr
    stvar $a, 123
    stvar $c, ""hello world""
    ret
}
";
        var body = Parse(code);
        var insts = body.EntryBlock.NonPhis().ToArray();

        Assert.True(insts[0] is VarAddrInst { Var: { Name: "b", IsExposed: true }});
        Assert.True(insts[1] is StoreVarInst { Var: { Name: "pin", IsPinned: true } v1, Value: Argument } && v1.Sig == PrimType.Int32.CreateArray());
    }

    [Fact]
    public void ParseConv()
    {
        string code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::TestCase(int x) {
Entry:
    byte a = conv #x
    byte b = conv.ovf.un #x
    float c = conv.un #x
    ret
}
";
        var body = Parse(code);
        var insts = body.EntryBlock.NonPhis().ToArray();

        Assert.True(insts[0] is ConvertInst { Value: Argument, CheckOverflow: false, SrcUnsigned: false } c1 && c1.ResultType == PrimType.Byte);
        Assert.True(insts[1] is ConvertInst { Value: Argument, CheckOverflow: true, SrcUnsigned: true } c2 && c2.ResultType == PrimType.Byte);
        Assert.True(insts[2] is ConvertInst { Value: Argument, CheckOverflow: false, SrcUnsigned: true } c3 && c3.ResultType == PrimType.Single);
    }

    [Fact]
    public void ParsePointers()
    {
        string code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::TestCase(int* ptr) {
Entry:
    int a = ldptr #ptr
    int b = ldptr.un.volatile #ptr
    stptr #ptr, 123 as int
    stptr.un.volatile #ptr, 123 as byte
    ret
}
";
        var body = Parse(code);
        var insts = body.EntryBlock.NonPhis().ToArray();

        const PointerFlags UnVol = PointerFlags.Unaligned | PointerFlags.Volatile;

        Assert.True(insts[0] is LoadPtrInst { Flags: 0, Address: Argument } ld1 && ld1.ElemType == PrimType.Int32);
        Assert.True(insts[1] is LoadPtrInst { Flags: UnVol, Address: Argument });

        Assert.True(insts[2] is StorePtrInst { Flags: 0, Address: Argument } st1 && st1.ElemType == PrimType.Int32);
        Assert.True(insts[3] is StorePtrInst { Flags: UnVol, Address: Argument } st2 && st2.ElemType == PrimType.Byte);
    }

    [Fact]
    public void MultiErrors()
    {
        var code = @"
import @ from DistIL.Tests.TestAsm

static void ParserDummy::M() {
Block1:
    ThisTypeDoesNotExist x = add 1, 1
    int y = call Int32::Parse(string: ""12"")
    ObviousSyntaxError
}";
        var parser = CreateParser(code);
        Assert.Throws<FormatException>(() => parser.ParseUnit());

        var errors = parser.Context.Errors.Select(e => e.GetDetailedMessage()).ToArray();
        Assert.True(errors.Length >= 2);
    }

    [Fact]
    public void Lexing()
    {
        var code = """
    Identifier : ->
    //single line comment
    /* multi line comment */
    -55 12345 123UL -12L 3.14159 0.75f
    "arbitrary string \" \n lorem ipsum"
    Block1
        Block2
        Abc
            Def
    End
    """;
        var lexer = new Lexer(new ParserContext(code, _modResolver));

        AssertNext(TokenType.Identifier, "Identifier");
        AssertNext(TokenType.Colon);
        AssertNext(TokenType.Arrow);
        //Comment should have been skipped
        AssertNext(TokenType.Literal, ConstInt.CreateI(-55));
        AssertNext(TokenType.Literal, ConstInt.CreateI(12345));
        AssertNext(TokenType.Literal, ConstInt.Create(PrimType.UInt64, 123));
        AssertNext(TokenType.Literal, ConstInt.CreateL(-12));
        AssertNext(TokenType.Literal, ConstFloat.CreateD(3.14159));
        AssertNext(TokenType.Literal, ConstFloat.CreateS(0.75f));
        AssertNext(TokenType.Literal, ConstString.Create("arbitrary string \" \n lorem ipsum"));

        AssertNext(TokenType.Identifier, "Block1");
        AssertNext(TokenType.Indent);
        AssertNext(TokenType.Identifier, "Block2");
        AssertNext(TokenType.Identifier, "Abc");
        AssertNext(TokenType.Indent);
        AssertNext(TokenType.Identifier, "Def");
        AssertNext(TokenType.Dedent);
        AssertNext(TokenType.Dedent);
        AssertNext(TokenType.Identifier, "End");

        AssertNext(TokenType.EOF);

        void AssertNext(TokenType type, object? value = null)
        {
            var token = lexer.Next();
            Assert.Equal(type, token.Type);
            Assert.Equal(value, token.Value);
        }
    }

    private IRParser CreateParser(string code)
    {
        return new IRParser(new FakeParserContext(code, _modResolver));
    }

    private MethodBody Parse(string code)
    {
        var parser = CreateParser(code);
        parser.ParseUnit();
        return parser.Context.DeclaredMethods.First(m => m.Definition.Name == "TestCase");
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
            var body = Utils.CreateDummyMethodBody(returnSig.Type, paramSig, attribs, name);
            DeclaredMethods.Add(body);
            return body;
        }
    }
}