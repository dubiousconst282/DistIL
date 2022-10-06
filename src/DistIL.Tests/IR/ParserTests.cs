using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.IR.Utils.Parser;

public class ParserTests
{
    readonly ModuleResolver _modResolver;
    readonly ModuleDef _corelib;

    public ParserTests()
    {
        _modResolver = new();
        _corelib = _modResolver.Resolve("System.Private.CoreLib", throwIfNotFound: true);
    }

    [Fact]
    internal void Test_ParseType()
    {
        var t_List = _corelib.FindType("System.Collections.Generic", "List`1", throwIfNotFound: true);

        Check("System.Int32", _corelib.SysTypes.Int32);
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
            var parser = new AstParser(new ParserContext(str, _modResolver));
            var actType = parser.ParseType();
            Assert.Equal(expType, actType);
        }
    }

    [Fact]
    internal void Test_ParseInst()
    {
        var t_Math = _corelib.FindType("System", "Math", throwIfNotFound: true);
        var t_DateTime = _corelib.FindType("System", "DateTime", throwIfNotFound: true);

        string code = @"
int x = phi [A -> -1], [B -> 2]
int y = mul x, 4
int z = call Math::Max(int: y, int: 0)
DateTime w = ldfld DateTime::UnixEpoch
goto z ? BB_01 : BB_02";

        var expAst = new List<InstNode>() {
            new InstNode("phi", new() {
                new IdNode("A"), new BoundNode(ConstInt.CreateI(-1)),
                new IdNode("B"), new BoundNode(ConstInt.CreateI(2)),
            }, PrimType.Int32, "x"),

            new InstNode("mul", new() {
                new IdNode("x"), new BoundNode(ConstInt.CreateI(4)),
            }, PrimType.Int32, "y"),

            new InstNode("call", new() {
                new BoundNode(t_Math.FindMethod("Max", new MethodSig(PrimType.Int32, PrimType.Int32, PrimType.Int32))!),
                new IdNode("y"),
                new BoundNode(ConstInt.CreateI(0))
            }, PrimType.Int32, "z"),

            new InstNode("ldfld", new() {
                new BoundNode(t_DateTime.FindField("UnixEpoch")!)
            }, t_DateTime, "w"),

            new InstNode("goto", new() { new IdNode("z"), new IdNode("BB_01"), new IdNode("BB_02"), } )
        };

        var parser = new AstParser(new ParserContext(code, _modResolver));
        foreach (var expInst in expAst) {
            var actInst = parser.ParseInst();
            Assert.Equal(expInst, actInst);
        }
    }

    [Fact]
    internal void Test_Materializer()
    {
        var body = Utils.CreateDummyMethodBody(PrimType.Int32, PrimType.Int32);
        string code = @"
Block1:
    int r1 = ldvar $x
    int r2 = add r1, #0
    stvar $x, r2
    goto 1 ? Block2 : Block3
Block2:
    int res = phi [Block1 -> r2], [Block2 -> -1]
    ret res
Block3: goto Block3
";
        IRParser.Populate(body, new ParserContext(code, _modResolver));

        Assert.Equal(3, body.NumBlocks);
        var insts = body.Instructions().ToArray();
        
        var varX = ((LoadVarInst)insts[0]).Var;
        Assert.True(insts[1] is BinaryInst { Op: BinaryOp.Add, Left: var addL, Right: Argument { Index: 0 } } && addL == insts[0]);
        Assert.True(insts[2] is StoreVarInst st && st.Var == varX && st.Value == insts[1]);
        Assert.True(insts[3] is BranchInst { Cond: ConstInt { Value: 1 } });
        Assert.True(insts[4] is PhiInst phi && phi.GetBlock(0) == insts[3].Block && phi.GetValue(0) == insts[1] && phi.GetValue(1) is ConstInt { Value: -1 });
        Assert.True(insts[5] is ReturnInst ret && ret.Value == insts[4]);
    }

    [Fact]
    public void Test_MultiErrors()
    {
        var code = @"
Block1:
    ThisTypeDoesNotExist x = add 1, 1
    int y = call Int32::Parse(string: ""12"")";
        var ctx = new ParserContext(code, _modResolver);
        var program = new AstParser(ctx).ParseProgram();

        var errors = ctx.Errors.Select(e => e.GetDetailedMessage()).ToArray();

        ;
    }
}