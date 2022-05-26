namespace DistIL.AsmIO;

using System.Collections.Immutable;
using System.Reflection.Metadata;

internal class TypeProvider : ISignatureTypeProvider<TypeDesc, GenericContext>, ICustomAttributeTypeProvider<TypeDesc>
{
    readonly ModuleLoader _loader;

    public TypeProvider(ModuleLoader loader)
    {
        _loader = loader;
    }

    public TypeDesc GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return typeCode switch {
            PrimitiveTypeCode.Void    => PrimType.Void,
            PrimitiveTypeCode.Boolean => PrimType.Bool,
            PrimitiveTypeCode.Char    => PrimType.Char,
            PrimitiveTypeCode.SByte   => PrimType.SByte,
            PrimitiveTypeCode.Byte    => PrimType.Byte,
            PrimitiveTypeCode.Int16   => PrimType.Int16,
            PrimitiveTypeCode.UInt16  => PrimType.UInt16,
            PrimitiveTypeCode.Int32   => PrimType.Int32,
            PrimitiveTypeCode.UInt32  => PrimType.UInt32,
            PrimitiveTypeCode.Int64   => PrimType.Int64,
            PrimitiveTypeCode.UInt64  => PrimType.UInt64,
            PrimitiveTypeCode.Single  => PrimType.Single,
            PrimitiveTypeCode.Double  => PrimType.Double,
            PrimitiveTypeCode.IntPtr  => PrimType.IntPtr,
            PrimitiveTypeCode.UIntPtr => PrimType.UIntPtr,
            PrimitiveTypeCode.String  => PrimType.String,
            PrimitiveTypeCode.Object  => PrimType.Object,
            PrimitiveTypeCode.TypedReference => PrimType.TypedRef,
            _ => throw new NotSupportedException()
        };
    }

    public TypeDesc GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        return (TypeDesc)_loader.GetType(handle);
    }

    public TypeDesc GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        return (TypeDesc)_loader.GetEntity(handle);
    }

    public TypeDesc GetTypeFromSpecification(MetadataReader reader, GenericContext context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return (TypeDesc)_loader.GetEntity(handle);
    }

    public TypeDesc GetSZArrayType(TypeDesc elementType)
    {
        return new ArrayType(elementType);
    }
    public TypeDesc GetArrayType(TypeDesc elementType, ArrayShape shape)
    {
        return new MDArrayType(elementType, shape.Rank, shape.LowerBounds, shape.Sizes);
    }

    public TypeDesc GetByReferenceType(TypeDesc elementType)
    {
        return new ByrefType(elementType);
    }
    public TypeDesc GetPointerType(TypeDesc elementType)
    {
        return new PointerType(elementType);
    }

    public TypeDesc GetPinnedType(TypeDesc elementType)
    {
        return new PinnedType_(elementType);
    }

    public TypeDesc GetFunctionPointerType(MethodSignature<TypeDesc> signature)
    {
        return new FuncPtrType(signature);
    }

    public TypeDesc GetGenericInstantiation(TypeDesc genericType, ImmutableArray<TypeDesc> typeArguments)
    {
        return genericType.GetSpec(new GenericContext(typeArguments));
    }

    public TypeDesc GetGenericMethodParameter(GenericContext context, int index)
    {
        return new GenericParamType(index, true);
    }
    public TypeDesc GetGenericTypeParameter(GenericContext context, int index)
    {
        return new GenericParamType(index, false);
    }

    public TypeDesc GetModifiedType(TypeDesc modifier, TypeDesc unmodifiedType, bool isRequired)
    {
        return unmodifiedType; //FIXME: implement this thing
    }

    public TypeDesc GetSystemType()
    {
        return _loader._mod.SysTypes.Type;
    }
    public bool IsSystemType(TypeDesc type)
    {
        return type == _loader._mod.SysTypes.Type;
    }

    public TypeDesc GetTypeFromSerializedName(string name)
    {
        throw new NotImplementedException();
    }
    public PrimitiveTypeCode GetUnderlyingEnumType(TypeDesc type)
    {
        var underlyingType = ((TypeDefOrSpec)type).UnderlyingEnumType!;
        return underlyingType.Kind switch {
            TypeKind.Bool    => PrimitiveTypeCode.Boolean,
            TypeKind.Char    => PrimitiveTypeCode.Char,
            TypeKind.SByte   => PrimitiveTypeCode.SByte,
            TypeKind.Byte    => PrimitiveTypeCode.Byte,
            TypeKind.Int16   => PrimitiveTypeCode.Int16,
            TypeKind.UInt16  => PrimitiveTypeCode.UInt16,
            TypeKind.Int32   => PrimitiveTypeCode.Int32,
            TypeKind.UInt32  => PrimitiveTypeCode.UInt32,
            TypeKind.Int64   => PrimitiveTypeCode.Int64,
            TypeKind.UInt64  => PrimitiveTypeCode.UInt64,
            TypeKind.Single  => PrimitiveTypeCode.Single,
            TypeKind.Double  => PrimitiveTypeCode.Double,
            TypeKind.IntPtr  => PrimitiveTypeCode.IntPtr,
            TypeKind.UIntPtr => PrimitiveTypeCode.UIntPtr,
            _ => throw new NotSupportedException()
        };
    }
}