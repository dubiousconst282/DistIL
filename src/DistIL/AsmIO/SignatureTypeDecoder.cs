namespace DistIL.AsmIO;

using System.Collections.Immutable;
using System.Reflection.Metadata;
using DistIL.IR;

public interface IGenericContext
{
    ImmutableArray<RType> GenericTypeParams { get; }
    ImmutableArray<RType> GenericMethodParams { get; }
}
public class SignatureTypeDecoder : ISignatureTypeProvider<RType, IGenericContext?>
{
    public ModuleDef Module { get; }

    public SignatureTypeDecoder(ModuleDef mod)
    {
        Module = mod;
    }

    public RType GetPrimitiveType(PrimitiveTypeCode typeCode)
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
            _ => throw new NotSupportedException()
        };
    }

    public RType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        Assert(reader == Module.Reader);
        return Module.GetType(handle);
    }

    public RType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        Assert(reader == Module.Reader);
        return Module.GetType(handle);
    }

    public RType GetTypeFromSpecification(MetadataReader reader, IGenericContext? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var typeSpec = reader.GetTypeSpecification(handle);
        var sig = typeSpec.DecodeSignature(this, genericContext);
        throw new NotImplementedException();
    }

    public RType GetSZArrayType(RType elementType)
    {
        return new ArrayType(elementType);
    }
    public RType GetArrayType(RType elementType, ArrayShape shape)
    {
        return new MDArrayType(elementType, shape.Rank, shape.LowerBounds, shape.Sizes);
    }

    public RType GetByReferenceType(RType elementType)
    {
        return new ByrefType(elementType);
    }
    public RType GetPointerType(RType elementType)
    {
        return new PointerType(elementType);
    }

    public RType GetPinnedType(RType elementType)
    {
        return new PinnedType_(elementType);
    }

    public RType GetFunctionPointerType(MethodSignature<RType> signature)
    {
        throw new NotImplementedException();
    }

    public RType GetGenericInstantiation(RType genericType, ImmutableArray<RType> typeArguments)
    {
        throw new NotImplementedException();
    }

    public RType GetGenericMethodParameter(IGenericContext? genericContext, int index)
    {
        throw new NotImplementedException();
    }
    public RType GetGenericTypeParameter(IGenericContext? genericContext, int index)
    {
        throw new NotImplementedException();
    }

    public RType GetModifiedType(RType modifier, RType unmodifiedType, bool isRequired)
    {
        throw new NotImplementedException();
    }
}

/// <summary> Represents a type for a local variable that holds a pinned GC reference. It should never be used directly. </summary>
public class PinnedType_ : CompoundType
{
    public override TypeKind Kind => ElemType.Kind;
    public override StackType StackType => ElemType.StackType;

    protected override string Postfix => "^";

    public PinnedType_(RType elemType)
        : base(elemType)
    {
    }

    public override bool Equals(RType? other) 
        => other is PinnedType_ o && o.ElemType == ElemType;
}