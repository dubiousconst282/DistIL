namespace DistIL.AsmIO;

using System.Collections.Immutable;
using System.Reflection.Metadata;

internal class TypeProvider : ISignatureTypeProvider<TypeDesc, GenericContext>
{
    readonly ModuleLoader _loader;

    public TypeProvider(ModuleLoader loader)
    {
        _loader = loader;
    }

    public static TypeDesc GetPrimitiveTypeFromCode(PrimitiveTypeCode typeCode)
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

    public TypeDesc GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        return GetPrimitiveTypeFromCode(typeCode);
    }

    public TypeDesc GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        return _loader.GetType(handle);
    }

    public TypeDesc GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        return (TypeDesc)_loader.GetEntity(handle);
    }

    public TypeDesc GetTypeFromSpecification(MetadataReader reader, GenericContext context, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return ((TypeSpec)_loader.GetEntity(handle)).GetSpec(context);
    }

    public TypeDesc GetSZArrayType(TypeDesc elementType)
    {
        return elementType.CreateArray();
    }
    public TypeDesc GetArrayType(TypeDesc elementType, ArrayShape shape)
    {
        return new MDArrayType(elementType, shape.Rank, shape.LowerBounds, shape.Sizes);
    }

    public TypeDesc GetByReferenceType(TypeDesc elementType)
    {
        return elementType.CreateByref();
    }
    public TypeDesc GetPointerType(TypeDesc elementType)
    {
        return elementType.CreatePointer();
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
        return context.GetArgument(index, true) ?? new GenericParamType(index, true);
    }
    public TypeDesc GetGenericTypeParameter(GenericContext context, int index)
    {
        return context.GetArgument(index, false) ?? new GenericParamType(index, false);
    }

    public TypeDesc GetModifiedType(TypeDesc modifier, TypeDesc unmodifiedType, bool isRequired)
    {
        return unmodifiedType; //FIXME: implement this thing
    }
}