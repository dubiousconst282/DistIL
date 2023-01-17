namespace DistIL.Tests.Passes;

using System.Diagnostics;

using DistIL.AsmIO;
using DistIL.Frontend;
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
        _testAsm = _modResolver.Resolve("TestAsm");
    }

    //TODO: these should ideally be theories for each decl (+ fixture for parsed sources) to make debugging easier

    [Fact]
    public void Test_SimplifyInsts()
        => RunTests("SimplifyInsts.ethil", new SimplifyInsts(_testAsm));

    [Fact]
    public void Test_Importer()
    {
        var decls = Utils.ParseMethodDecls("Passes/Cases/Importer.ethil", _modResolver);
        var actualType = _modResolver.Resolve("TestAsm.IL").FindType(null, "ImporterCases");

        foreach (var (name, expectedBody) in decls) {
            Debug.Assert(name.EndsWith(".expected"));

            var actualDef = (MethodDef)actualType.FindMethod(name[0..^".expected".Length]);
            var actualBody = ILImporter.ImportCode(actualDef);

            Assert.True(CompareBodies(expectedBody, actualBody), $"Case '{name}' doesn't match expected body");
        }
    }

    private void RunTests(string filename, MethodPass pass)
    {
        var decls = Utils.ParseMethodDecls("Passes/Cases/" + filename, _modResolver);
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

        sourceA = sourceA.Replace("DummyClass::", "::").Replace(".expected(", "(");
        sourceB = sourceB.Replace("DummyClass::", "::").Replace(".expected(", "(");

        return sourceA == sourceB; //ugly hack until we have a IRComparer
    }
}