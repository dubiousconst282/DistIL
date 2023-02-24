namespace DistIL.Tests;

using DistIL.AsmIO;

[CollectionDefinition("ModuleResolver")]
public class ModuleResolverFixture : ICollectionFixture<ModuleResolverFixture>
{
    public ModuleResolver Resolver { get; }

    public ModuleResolverFixture()
    {
        Resolver = new();
        Resolver.AddTrustedSearchPaths();
    }
}