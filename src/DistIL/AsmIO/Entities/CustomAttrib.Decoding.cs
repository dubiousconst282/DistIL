namespace DistIL.AsmIO;

using System.Collections.Immutable;
using System.Reflection.Metadata;

using PropArray = ImmutableArray<CustomAttribProp>;
using ValueArray = ImmutableArray<object?>;

partial class CustomAttrib
{
    private unsafe void DecodeBlob()
    {
        fixed (byte* dataPtr = _encodedBlob) {
            var reader = new BlobReader(dataPtr, _encodedBlob!.Length);

            //Prolog
            if (reader.ReadUInt16() != 0x0001) {
                throw new BadImageFormatException();
            }
            _fixedArgs = DecodeFixedArgs(ref reader);
            _namedArgs = DecodeNamedArgs(ref reader);
        }
    }

    private ValueArray DecodeFixedArgs(ref BlobReader reader)
    {
        int count = Constructor.ParamSig.Count;
        if (count < 2) {
            return ValueArray.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<object?>(count - 1);
        for (int i = 1; i < count; i++) {
            builder.Add(DecodeElement(ref reader, Constructor.ParamSig[i].Type));
        }
        return builder.MoveToImmutable();
    }

    private PropArray DecodeNamedArgs(ref BlobReader reader)
    {
        int count = reader.ReadUInt16();
        if (count == 0) {
            return PropArray.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<CustomAttribProp>(count);
        for (int i = 0; i < count; i++) {
            const byte kField = 0x53, kProp = 0x54;

            byte memberType = reader.ReadByte();
            if (memberType is not (kField or kProp)) {
                throw new BadImageFormatException();
            }
            var propType = DecodeElementType(ref reader);

            builder.Add(new CustomAttribProp() {
                IsField = memberType == kField,
                Type = propType,
                Name = reader.ReadSerializedString()!,
                Value = DecodeElement(ref reader, propType)
            });
        }
        return builder.MoveToImmutable();
    }

    private TypeDesc DecodeElementType(ref BlobReader reader)
    {
        var code = reader.ReadSerializationTypeCode();

        switch (code) {
            case >= SerializationTypeCode.Boolean and <= SerializationTypeCode.String: {
                return PrimType.GetFromSrmCode((PrimitiveTypeCode)code);
            }
            case SerializationTypeCode.SZArray: {
                var elemType = DecodeElementType(ref reader);
                return elemType.CreateArray();
            }
            case SerializationTypeCode.Type: {
                return _parentModule!.Resolver.SysTypes.Type;
            }
            case SerializationTypeCode.TaggedObject: {
                return PrimType.Object;
            }
            case SerializationTypeCode.Enum: {
                var enumType = reader.ReadSerializedString();
                return ParseSerializedType(_parentModule!, enumType!);
            }
            default: throw new BadImageFormatException();
        }
    }

    private object? DecodeElement(ref BlobReader reader, TypeDesc type)
    {
        var sys = _parentModule!.Resolver.SysTypes;
        if (type is ArrayType) {
            int numElems = reader.ReadInt32();
            if (numElems < 0) {
                return null;
            }
            var elemType = GetRealType(type.ElemType!, sys);
            var array = Array.CreateInstance(elemType, numElems);
            for (int i = 0; i < numElems; i++) {
                array.SetValue(DecodeElement(ref reader, type.ElemType!), i);
            }
            return array;
        }
        if (type == sys.Type) {
            string? typeName = reader.ReadSerializedString();
            return ParseSerializedType(_parentModule!, typeName!);
        }
#pragma warning disable format
        return type.Kind switch {
            TypeKind.Bool   => reader.ReadBoolean(),
            TypeKind.Char   => reader.ReadChar(),
            TypeKind.SByte  => reader.ReadSByte(),
            TypeKind.Byte   => reader.ReadByte(),
            TypeKind.Int16  => reader.ReadInt16(),
            TypeKind.UInt16 => reader.ReadUInt16(),
            TypeKind.Int32  => reader.ReadInt32(),
            TypeKind.UInt32 => reader.ReadUInt32(),
            TypeKind.Int64  => reader.ReadInt64(),
            TypeKind.UInt64 => reader.ReadUInt64(),
            TypeKind.Single => reader.ReadSingle(),
            TypeKind.Double => reader.ReadDouble(),
            TypeKind.String => reader.ReadSerializedString(),
            //Boxed value
            _ when type == PrimType.Object => DecodeElement(ref reader, DecodeElementType(ref reader)),
            _ => throw new BadImageFormatException()
        };
    }

    private static Type GetRealType(TypeDesc type, SystemTypes sys)
        => type.Kind switch {
            TypeKind.Bool   => typeof(Boolean),
            TypeKind.Char   => typeof(Char),
            TypeKind.SByte  => typeof(SByte),
            TypeKind.Byte   => typeof(Byte),
            TypeKind.Int16  => typeof(Int16),
            TypeKind.UInt16 => typeof(UInt16),
            TypeKind.Int32  => typeof(Int32),
            TypeKind.UInt32 => typeof(UInt32),
            TypeKind.Int64  => typeof(Int64),
            TypeKind.UInt64 => typeof(UInt64),
            TypeKind.Single => typeof(Single),
            TypeKind.Double => typeof(Double),
            TypeKind.String => typeof(String),
            _ when type == sys.Type => typeof(TypeDesc),
            _ => throw new InvalidOperationException()
        };
#pragma warning restore format
}
