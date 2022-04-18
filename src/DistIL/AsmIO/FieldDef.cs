namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DistIL.IR;

public class FieldDef : Field, MemberDef
{
    public override TypeDef DeclaringType { get; }
    
    public ModuleDef Module => DeclaringType.Module;

    public FieldAttributes Attribs { get; }

    public int RVA { get; }
    public object? DefaultValue { get; }

    public FieldDef(ModuleDef mod, FieldDefinitionHandle handle)
    {
        var reader = mod.Reader;
        var def = reader.GetFieldDefinition(handle);

        DeclaringType = mod.GetType(def.GetDeclaringType());
        Attribs = def.Attributes;
        Type = def.DecodeSignature(mod.TypeDecoder, null);

        Name = reader.GetString(def.Name);

        IsStatic = Attribs.HasFlag(FieldAttributes.Static);

        if (Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
            RVA = def.GetRelativeVirtualAddress();
        }
        if (Attribs.HasFlag(FieldAttributes.HasDefault)) {
            DefaultValue = DecodeConst(reader, def.GetDefaultValue());
        }
    }

    /// <summary> Returns the memory block of the field RVA data, assuming (Attribs has HasFieldRVA). </summary>
    public PEMemoryBlock GetStaticDataBlock()
    {
        return DeclaringType.Module.PE.GetSectionData(RVA);
    }
    /// <summary> 
    /// Returns a span of the field RVA data, assuming (Attribs has HasFieldRVA). 
    /// It is backed by an unmanaged pointer, which is valid until the declaring type module PE is dispoed.
    /// </summary>
    public unsafe ReadOnlySpan<byte> GetStaticData()
    {
        var data = GetStaticDataBlock();
        return new(data.Pointer, data.Length);
    }

    private static object? DecodeConst(MetadataReader reader, ConstantHandle handle)
    {
        var cst = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(cst.Value);

        #pragma warning disable format
        return cst.TypeCode switch {
            ConstantTypeCode.Boolean    => blob.ReadBoolean(),
            ConstantTypeCode.Char       => blob.ReadChar(),
            ConstantTypeCode.SByte      => blob.ReadSByte(),
            ConstantTypeCode.Byte       => blob.ReadByte(),
            ConstantTypeCode.Int16      => blob.ReadInt16(),
            ConstantTypeCode.UInt16     => blob.ReadUInt16(),
            ConstantTypeCode.Int32      => blob.ReadInt32(),
            ConstantTypeCode.UInt32     => blob.ReadUInt32(),
            ConstantTypeCode.Int64      => blob.ReadInt64(),
            ConstantTypeCode.UInt64     => blob.ReadUInt64(),
            ConstantTypeCode.Single     => blob.ReadSingle(),
            ConstantTypeCode.Double     => blob.ReadDouble(),
            ConstantTypeCode.String     => blob.ReadSerializedString(),
            ConstantTypeCode.NullReference => null,
            _ => throw new NotSupportedException()
        };
        #pragma warning restore format
    }
}
