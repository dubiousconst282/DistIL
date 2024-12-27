namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public abstract class ResourceDesc : ModuleEntity
{
    public string Name { get; set; }
    public ManifestResourceAttributes Attribs { get; set; }
    public ModuleDef Module { get; }

    internal ResourceDesc(ModuleDef module, string name, ManifestResourceAttributes attrs)
    {
        Module = module;
        Name = name;
        Attribs = attrs;
    }

    internal IList<CustomAttrib>? _customAttribs = null;

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);
}
public class EmbeddedResource : ResourceDesc
{
    internal byte[]? _userData = null;
    internal int _offsetInResourceDir = 0;

    public EmbeddedResource(ModuleDef module, string name, ManifestResourceAttributes attrs, byte[] data)
        : base(module, name, attrs)
    {
        _userData = data;
    }
    internal EmbeddedResource(ModuleDef module, string name, ManifestResourceAttributes attrs, int offsetInResourceDir)
        : base(module, name, attrs)
    {
        _offsetInResourceDir = offsetInResourceDir;
    }

    public unsafe ReadOnlySpan<byte> GetData()
    {
        if (_userData != null) {
            return _userData;
        }
        
        var pe = Module._loader!._pe;
        PEMemoryBlock section = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
        BlobReader blob = section.GetReader(_offsetInResourceDir, section.Length - _offsetInResourceDir);
        int length = blob.ReadInt32();

        if (length < 0 || length >= blob.Length - 4) {
            throw new BadImageFormatException();
        }
        return new ReadOnlySpan<byte>(blob.CurrentPointer, length);
    }

    /// <summary> Overwrites the resource data, taking shared ownership of the given array (meaning mutations are reflected). </summary>
    public void SetData(byte[] data)
    {
        _userData = data;
    }
}
public class LinkedResource(ModuleDef module, string name, ManifestResourceAttributes attrs,
                            string fileName, byte[] hash, bool containsMetadata = false)
    : ResourceDesc(module, name, attrs)
{
    public string FileName { get; set; } = fileName;
    public byte[] Hash { get; set; } = hash;
    public bool ContainsMetadata { get; set; } = containsMetadata; // II.23.1.6
}