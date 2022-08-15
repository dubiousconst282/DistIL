using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.IR.Utils.Parser;

public class ParserTests
{
    [Theory, MemberData(nameof(GetTypeTests))]
    internal void ParseType(string str, TypeNode expType)
    {
        var parser = new AstParser(new ParserContext(str, null));
        var actType = parser.ParseType();
        Assert.Equal(expType, actType);
    }

    [Theory, MemberData(nameof(GetInstTests))]
    internal void ParseInst(string str, IEnumerable<InstNode> expInsts)
    {
        var parser = new AstParser(new ParserContext(str, null));
        foreach (var expInst in expInsts) {
            var actInst = parser.ParseInst();
            Assert.Equal(expInst, actInst);
        }
    }

    [Fact]
    internal void Materialize()
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
        IRParser.Populate(body, new ParserContext(code, null));

        Assert.Equal(3, body.NumBlocks);
        var insts = body.Instructions().ToArray();
        
        var varX = ((LoadVarInst)insts[0]).Var;
        Assert.True(insts[1] is BinaryInst { Op: BinaryOp.Add, Left: var addL, Right: Argument { Index: 0 } } && addL == insts[0]);
        Assert.True(insts[2] is StoreVarInst st && st.Var == varX && st.Value == insts[1]);
        Assert.True(insts[3] is BranchInst { Cond: ConstInt { Value: 1 } });
        Assert.True(insts[4] is PhiInst phi && phi.GetBlock(0) == insts[3].Block && phi.GetValue(0) == insts[1] && phi.GetValue(1) is ConstInt { Value: -1 });
        Assert.True(insts[5] is ReturnInst ret && ret.Value == insts[4]);
    }

    public static IEnumerable<object[]> GetTypeTests()
    {
        yield return new object[] { "System.Int32", new BasicTypeNode("System.Int32") };
        yield return new object[] { "int[]", new ArrayTypeNode(new BasicTypeNode("int")) };
        yield return new object[] { "int[][]", new ArrayTypeNode(new ArrayTypeNode(new BasicTypeNode("int"))) };
        yield return new object[] { "int*[]&", new ByrefTypeNode(new ArrayTypeNode(new PointerTypeNode(new BasicTypeNode("int")))) };
        yield return new object[] {
            "System.Collections.Generic.List`1+Enumerator[string[]]", 
            new TypeSpecNode(
                new NestedTypeNode(new BasicTypeNode("System.Collections.Generic.List`1"), "Enumerator"),
                new TypeNode[] { new ArrayTypeNode(new BasicTypeNode("string")) }
            )
        };
        yield return new object[] {
            "NS.A`1+B`1[int[], int][]&",
            new ByrefTypeNode(new ArrayTypeNode(
                new TypeSpecNode(
                    new NestedTypeNode(new BasicTypeNode("NS.A`1"), "B`1"),
                    new TypeNode[] {
                        new ArrayTypeNode(new BasicTypeNode("int")),
                        new BasicTypeNode("int")
                    }
                )
            ))
        };
    }

    public static IEnumerable<object[]> GetInstTests()
    {
        yield return new object[] {
            "int x = phi [A -> -1], [B -> 2]\n" +
            "int y = mul x, 4\n" +
            "int z = call Math::Abs(int: y)\n" +
            "DateTime w = ldfld DateTime::UnixEpoch\n" +
            "goto z ? BB_01 : BB_02",
            new List<InstNode>() {
                new InstNode("phi", new() {
                    new IdNode("A"), new ConstNode(ConstInt.CreateI(-1)),
                    new IdNode("B"), new ConstNode(ConstInt.CreateI(2)),
                }, new BasicTypeNode("int"), "x"),

                new InstNode("mul", new() {
                    new IdNode("x"), new ConstNode(ConstInt.CreateI(4)),
                }, new BasicTypeNode("int"), "y"),

                new InstNode("call", new() {
                    new MethodNode(new BasicTypeNode("Math"), "Abs", null, new BasicTypeNode("int"), new() { new BasicTypeNode("int") }),
                    new IdNode("y"),
                }, new BasicTypeNode("int"), "z"),

                new InstNode("ldfld", new() {
                    new FieldNode(new BasicTypeNode("DateTime"), "UnixEpoch")
                },
                new BasicTypeNode("DateTime"), "w"),

                new InstNode("goto", new() { new IdNode("z"), new IdNode("BB_01"), new IdNode("BB_02"), } )
            }
        };
    }
}