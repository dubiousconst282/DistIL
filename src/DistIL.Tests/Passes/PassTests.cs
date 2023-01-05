namespace DistIL.Tests.Passes;

using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;
using DistIL.Passes;

[Collection("ModuleResolver")]
public class PassTests
{
    readonly ModuleResolver _modResolver;
    readonly ModuleDef _testAsm;

    public PassTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
        _testAsm = _modResolver.Resolve("DistIL.Tests.TestAsm", throwIfNotFound: true);
    }

    [Fact]
    public void Test_SimplifyInsts()
        => RunTest("SimplifyInsts.ethil", new SimplifyInsts(_testAsm));

    private void RunTest(string filename, MethodPass pass)
    {
        var source = File.ReadAllText("Passes/Cases/" + filename);
        var ctx = new FakeParserContext(source, _modResolver);
        new IRParser(ctx).ParseUnit();

        var decls = ctx.DeclaredMethods.ToDictionary(e => e.Definition.Name);
        var comp = new Compilation(_testAsm, new VoidLogger(), new CompilationSettings());

        foreach (var (name, body) in decls) {
            if (name.EndsWith(".expected")) continue;

            var expectedBody = decls[name + ".expected"];

            pass.Run(new MethodTransformContext(comp, body));

            Assert.True(CompareBodies(expectedBody, body), $"Case '{name}' doesn't match expected body");
        }
    }

    private static bool CompareBodies(MethodBody expectedBody, MethodBody actualBody)
    {
        var sw = new StringWriter();

        IRPrinter.ExportPlain(expectedBody, sw);
        string sourceA = sw.ToString();
        
        sw.GetStringBuilder().Clear();
        IRPrinter.ExportPlain(actualBody, sw);
        string sourceB = sw.ToString();

        return sourceA == sourceB; //ugly hack until we have a IRComparer
    }
}