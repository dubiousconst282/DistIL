namespace DistIL.Tests.Passes;

using DistIL.AsmIO;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

[Collection("ModuleResolver")]
public class PassRegressionTests
{
    readonly ModuleResolver _modResolver;
    readonly ModuleDef _testAsm;

    public PassRegressionTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
        _testAsm = _modResolver.Resolve("TestAsm");
    }

    // TODO: these should ideally be theories for each decl (+ fixture for parsed sources) to make debugging easier

    [Fact]
    public void Test_SimplifyInsts() => CheckEthil("SimplifyInsts.ethil", new SimplifyInsts(_modResolver));

    [Fact]
    public void Test_SimplifyCFG() => CheckEthil("SimplifyCFG.ethil", new SimplifyCFG());

    [Fact]
    public void Test_LoopStrengthReduction() => CheckEthil("LoopStrengthReduction.ethil", new LoopStrengthReduction());

    [Fact]
    public void Test_ValueNumbering() => CheckEthil("ValueNumbering.ethil", new ValueNumbering());

    [Fact]
    public void Test_AssertionProp() => CheckEthil("AssertionProp.ethil", new AssertionProp());

    private void CheckEthil(string filename, IMethodPass pass)
    {
        var selfType = _testAsm.CreateType("RegressionTests", $"_Test_{Path.GetFileNameWithoutExtension(filename)}");
        string source = File.ReadAllText("Passes/Cases/" + filename);

        string fixedSource = $"import RegressionTests from TestAsm\nimport Self = {selfType.Name}\n\n{source}";
        var ctx = IRParser.Populate(fixedSource, _modResolver);
        ctx.ThrowIfError();

        var decls = ctx.DeclaredMethods.ToDictionary(e => e.Definition.Name);

        var comp = new Compilation(_testAsm, new VoidLogger(), new CompilationSettings());
        var sw = new StringWriter();

        foreach (var (name, body) in decls) {
            pass.Run(new MethodTransformContext(comp, body));

            IRPrinter.ExportPlain(body, sw);
            sw.Write("\n\n");
        }

        var result = FileChecker.Check(source, sw.ToString(), StringComparison.Ordinal);
        if (!result.IsSuccess) {
            Directory.CreateDirectory("regress_fail");
            File.WriteAllText($"regress_fail/{filename}.txt", sw.ToString());
            
            Assert.True(result.IsSuccess, result.Failures[0].Message);
        }
    }

    // TODO: Fix importer cases
}