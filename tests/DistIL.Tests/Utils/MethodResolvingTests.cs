namespace DistIL.Tests.Util;

using System.Text;

using DistIL.AsmIO;

[Collection("ModuleResolver")]
public class MethodResolvingTests
{
    readonly ModuleResolver _modResolver;

    public MethodResolvingTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;

        _modResolver.Import(typeof(StringBuilder));
    }

    [Fact]
    public void Test_MethodResolving()
    {
        var selector = "System.Text.StringBuilder::AppendLine(this, System.String)";
        var method = _modResolver.FindMethod(selector);

        Assert.NotNull(method);
        Assert.Equal("System.Text", method.DeclaringType.Namespace);
        Assert.Equal("StringBuilder", method.DeclaringType.Name);
        Assert.Equal("AppendLine", method.Name);
        Assert.Equal("StringBuilder", method.ReturnType.Name);
        Assert.Equal(2, method.ParamSig.Count);
        Assert.Equal("StringBuilder", method.ParamSig[0].Type.Name);
        Assert.Equal("String", method.ParamSig[1].Type.Name);
    }
}