namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

public class ModuleWriter
{
    readonly ModuleDef _mod;
    readonly MetadataBuilder _builder;
    readonly BlobBuilder _ilStream;
    private BlobBuilder? _fieldDataStream;
    private BlobBuilder? _managedResourceStream;
    MethodDefinitionHandle _entryPoint;

    private Dictionary<EntityDef, EntityHandle> _handleMap = new();

    public ModuleWriter(ModuleDef mod)
    {
        _mod = mod;
        _builder = new MetadataBuilder();
        _ilStream = new BlobBuilder();
    }

    public void Serialize(BlobBuilder peBlob)
    {
        //https://github.com/Lokad/ILPack/blob/master/src/AssemblyGenerator.cs
        var name = _mod.Name;
        var mainAsmHandle = _builder.AddAssembly(
            AddString(name.Name!),
            name.Version!,
            AddString(name.CultureName),
            AddBlob(name.GetPublicKey()),
            (AssemblyFlags)name.Flags,
            (AssemblyHashAlgorithm)name.HashAlgorithm
        );

        SerializePE(peBlob);
    }

    private void SerializeTypes()
    {
        foreach (var type in _mod.GetDefinedTypes()) {
            /*var handle = _builder.AddTypeDefinition(
                type.Attribs, 
                AddString(type.Namespace),
                AddString(type.Name), 
*/
        }
    }

    private StringHandle AddString(string? str)
    {
        return str == null ? default : _builder.GetOrAddString(str);
    }
    private BlobHandle AddBlob(byte[]? data)
    {
        return data == null ? default : _builder.GetOrAddBlob(data);
    }

    private void SerializePE(BlobBuilder peBlob)
    {
        var hdrs = _mod.PE.PEHeaders;
        var peHdr = hdrs.PEHeader!;
        var coffHdr = hdrs.CoffHeader;
        var corHdr = hdrs.CorHeader;

        var header = new PEHeaderBuilder(
            machine: coffHdr.Machine,
            sectionAlignment: peHdr.SectionAlignment,
            fileAlignment: peHdr.FileAlignment,
            imageBase: peHdr.ImageBase,
            majorLinkerVersion: peHdr.MajorLinkerVersion,
            minorLinkerVersion: peHdr.MinorLinkerVersion,
            majorOperatingSystemVersion: peHdr.MajorOperatingSystemVersion,
            minorOperatingSystemVersion: peHdr.MinorOperatingSystemVersion,
            majorImageVersion: peHdr.MajorImageVersion,
            minorImageVersion: peHdr.MinorImageVersion,
            majorSubsystemVersion: peHdr.MajorSubsystemVersion,
            minorSubsystemVersion: peHdr.MinorSubsystemVersion,
            subsystem: peHdr.Subsystem,
            dllCharacteristics: peHdr.DllCharacteristics,
            imageCharacteristics: coffHdr.Characteristics,
            sizeOfStackReserve: peHdr.SizeOfStackReserve,
            sizeOfStackCommit: peHdr.SizeOfStackCommit,
            sizeOfHeapReserve: peHdr.SizeOfHeapReserve,
            sizeOfHeapCommit: peHdr.SizeOfHeapCommit
        );
        var peBuilder = new ManagedPEBuilder(
            header: header,
            metadataRootBuilder: new MetadataRootBuilder(_builder),
            ilStream: _ilStream,
            mappedFieldData: _fieldDataStream,
            managedResources: _managedResourceStream,
            entryPoint: _entryPoint
        );
        peBuilder.Serialize(peBlob);
    }
}