namespace DistIL.Tests.Util;

using System.Text;

using DistIL.AsmIO;
using DistIL.IR;

[Collection("ModuleResolver")]
public class MethodResolvingTests
{
    readonly ModuleResolver _modResolver;

    public MethodResolvingTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;

        _modResolver.Import(typeof(StringBuilder));
        _modResolver.Import(typeof(Console));
    }

    [Fact]
    public void Test_MethodResolving()
    {
        var selector = "System.Text.StringBuilder::AppendLine(this, string)";
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

    [Fact]
    public void Test_MethodResolvingByValues()
    {
        var selector = "System.Console::WriteLine";
        var method = _modResolver.FindMethod(selector, [ConstString.Create("Hello World")]);

        Assert.NotNull(method);
        Assert.Equal("System", method.DeclaringType.Namespace);
        Assert.Equal("Console", method.DeclaringType.Name);
        Assert.Equal("WriteLine", method.Name);
        Assert.Equal("Void", method.ReturnType.Name);
        Assert.Equal(1, method.ParamSig.Count);
        Assert.Equal("String", method.ParamSig[0].Type.Name);
    }
}