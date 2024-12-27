namespace DistIL.Tests.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using DistIL.AsmIO;

[Collection("ModuleResolver")]
public class ManifestResourceTests
{
    readonly ModuleResolver _modResolver;

    public ManifestResourceTests(ModuleResolverFixture mrf)
    {
        _modResolver = mrf.Resolver;
    }

    [Fact]
    public void ResourceLoading()
    {
        var module = _modResolver.Resolve("TestAsm");

        var resource = module.GetManifestResource("TestAsm.Resources.lorem_ipsum.txt") as EmbeddedResource;
        Assert.NotNull(resource);

        var data = Encoding.UTF8.GetString(resource.GetData());
        Assert.StartsWith("Lorem ipsum dolor sit amet", data);

        var linkedResource = module.GetManifestResource("byte_sequence.bin") as LinkedResource;
        Assert.NotNull(linkedResource);
        Assert.Equal(Convert.FromHexString("4916D6BDB7F78E6803698CAB32D1586EA457DFC8"), linkedResource.Hash); // SHA1
    }

    
    [Fact]
    public void ResourceWriting()
    {
        var asm = _modResolver.Create("ManifestResource_TestStub");

        var resource1 = asm.CreateEmbeddedResource("data1", [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        var resource2 = new LinkedResource(asm, "ref2", System.Reflection.ManifestResourceAttributes.Public, "lorem_ipsum.txt", []);
        asm.ManifestResources.Add(resource2);

        Assert.Throws<InvalidOperationException>(() => asm.CreateEmbeddedResource("data1", []));

        using var stream = new MemoryStream();
        asm.Save(stream, null, "stub.dll");

        stream.Position = 0;

        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.Equal(2, reader.ManifestResources.Count);

        var mfs = reader.ManifestResources.Select(h => reader.GetManifestResource(h)).ToArray();
        Assert.Equal("data1", reader.GetString(mfs[0].Name));
        Assert.Equal(System.Reflection.ManifestResourceAttributes.Public, mfs[0].Attributes);
        Assert.True(mfs[0].Implementation.IsNil);

        Assert.Equal("ref2", reader.GetString(mfs[1].Name));
        Assert.Equal(HandleKind.AssemblyFile, mfs[1].Implementation.Kind);
    }
}