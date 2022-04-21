namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

public class FieldDef : Field, MemberDef
{
    public override TypeDef DeclaringType { get; }
    public ModuleDef Module => DeclaringType.Module;
    public EntityHandle Handle { get; }

    public FieldAttributes Attribs { get; }

    public object? DefaultValue { get; }

    /// <summary> The field layout offset (e.g. x in [FieldOffset(x)]), or -1 if not available. </summary>
    public int LayoutOffset { get; set; }

    /// <summary> Static data associated with the field. Attribs must have HasFieldRVA, and array length must be equal to the type layout size. </summary>
    public byte[]? MappedData { get; set; }

    public override bool IsStatic => Attribs.HasFlag(FieldAttributes.Static);

    public FieldDef(ModuleDef mod, FieldDefinitionHandle handle)
    {
        Handle = handle;
        
        var reader = mod.Reader;
        var def = reader.GetFieldDefinition(handle);

        Attribs = def.Attributes;
        Type = def.DecodeSignature(mod.TypeDecoder, null);
        Name = reader.GetString(def.Name);

        DeclaringType = mod.GetType(def.GetDeclaringType());

        if (Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
            int rva = def.GetRelativeVirtualAddress();
            var data = mod.PE.GetSectionData(rva);
            unsafe { MappedData = new Span<byte>(data.Pointer, GetMappedDataSize(Type)).ToArray(); }
        }
        if (Attribs.HasFlag(FieldAttributes.HasDefault)) {
            DefaultValue = DecodeConst(reader, def.GetDefaultValue());
        }
        LayoutOffset = def.GetOffset();

        Ensure(def.GetMarshallingDescriptor().IsNil); //not impl
        Ensure(def.GetCustomAttributes().Count == 0);
    }

    private static int GetMappedDataSize(RType type)
    {
        switch (type.Kind) {
            case TypeKind.Bool:
            case TypeKind.SByte:
            case TypeKind.Byte:
                return 1;
            case TypeKind.Char:
            case TypeKind.Int16:
            case TypeKind.UInt16:
                return 2;
            case TypeKind.Int32:
            case TypeKind.UInt32:
            case TypeKind.Single:
                return 4;
            case TypeKind.Int64:
            case TypeKind.UInt64:
            case TypeKind.Double:
                return 8;
            default:
                if (type is TypeDef def) {
                    var layout = def.Layout;
                    Ensure(!layout.IsDefault); //not impl
                    return layout.Size;
                }
                return 0;
        }
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
