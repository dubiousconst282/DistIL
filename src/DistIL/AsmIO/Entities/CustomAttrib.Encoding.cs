namespace DistIL.AsmIO;

using System.Reflection.Metadata;

partial class CustomAttrib
{
    private byte[] EncodeBlob()
    {
        var writer = new BlobBuilder();
        writer.WriteUInt16(0x0001); //prolog

        EncodeFixedArgs(writer);
        EncodeNamedArgs(writer);
        return writer.ToArray();
    }

    private void EncodeFixedArgs(BlobBuilder writer)
    {
        for (int i = 1; i < Constructor.ParamSig.Count; i++) {
            EncodeElement(writer, Constructor.ParamSig[i].Type, _fixedArgs[i - 1]);
        }
    }

    private void EncodeNamedArgs(BlobBuilder writer)
    {
        writer.WriteUInt16(checked((ushort)_namedArgs.Length));

        if (_namedArgs.Length > 0) {
            throw new NotImplementedException();
        }
    }

    private void EncodeElement(BlobBuilder writer, TypeDesc type, object? value)
    {
        if (type is ArrayType) {
            if (value == null) {
                writer.WriteInt32(-1);
                return;
            }
            var array = (Array)value!;

            writer.WriteInt32(array.Length);
            for (int i = 0; i < array.Length; i++) {
                EncodeElement(writer, type.ElemType!, array.GetValue(i));
            }
            return;
        }
        else if (IsSystemType(type)) {
            //string? typeName = reader.ReadSerializedString();
            //return ParseSerializedType(_parentModule!, typeName!);
            throw new NotImplementedException(); //SerializeType() is broken
        }
#pragma warning disable format
        else switch (type.Kind) {
            case TypeKind.Bool:   writer.WriteBoolean((bool)value!); break;
            case TypeKind.Char:   writer.WriteUInt16((char)value!); break;
            case TypeKind.SByte:  writer.WriteSByte((sbyte)value!); break;
            case TypeKind.Byte:   writer.WriteByte((byte)value!); break;
            case TypeKind.Int16:  writer.WriteInt16((short)value!); break;
            case TypeKind.UInt16: writer.WriteUInt16((ushort)value!); break;
            case TypeKind.Int32:  writer.WriteInt32((int)value!); break;
            case TypeKind.UInt32: writer.WriteUInt32((uint)value!); break;
            case TypeKind.Int64:  writer.WriteInt64((long)value!); break;
            case TypeKind.UInt64: writer.WriteUInt64((ulong)value!); break;
            case TypeKind.Single: writer.WriteSingle((float)value!); break;
            case TypeKind.Double: writer.WriteDouble((double)value!); break;
            case TypeKind.String: writer.WriteSerializedString((string?)value); break;
            //Boxed value
            case var _ when type == PrimType.Object:
                throw new NotImplementedException();

            default: throw new NotSupportedException();
        }
    }

    private static bool IsSystemType(TypeDesc type)
    {
        return type is TypeDef def && def == def.Module.Resolver.SysTypes.Type;
    }
}