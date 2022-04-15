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
    public bool HasDefaultValue { get; }

    public FieldDef(ModuleDef mod, FieldDefinitionHandle handle)
    {
        var reader = mod.Reader;
        var def = reader.GetFieldDefinition(handle);

        DeclaringType = mod.GetType(def.GetDeclaringType());
        Attribs = def.Attributes;
        Type = def.DecodeSignature(mod.TypeDecoder, null);

        Name = reader.GetString(def.Name);
        RVA = def.GetRelativeVirtualAddress();

        IsStatic = (Attribs & FieldAttributes.Static) != 0;

        if (def.GetDefaultValue() is { IsNil: false } valHnd) {
            DefaultValue = DecodeConst(reader, valHnd);
            HasDefaultValue = true;
        }
    }

    public PEMemoryBlock GetInlineData()
    {
        return DeclaringType.Module.PE.GetSectionData(RVA);
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
