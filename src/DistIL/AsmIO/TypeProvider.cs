namespace DistIL.AsmIO;

using System.Collections.Immutable;
using System.Reflection;
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

    public TypeDesc GetSystemType()
    {
        return _loader._mod.SysTypes.Type;
    }
    public bool IsSystemType(TypeDesc type)
    {
        return type == _loader._mod.SysTypes.Type;
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

    //Adapted from AstParser.ParseType()
    //Type = Identifier  ("+"  Identifier)*  ("["  Seq{Type}  "]")?  ("[]" | "*" | "&")*
    //  "NS.A`1+B`1[int[], int][]&"  ->  "NS.A.B<int[], int>[]&"
    public TypeDesc GetTypeFromSerializedName(string str)
    {
        var module = _loader._mod;
        //Correctly handle _assembly qualified type names_, such as: 
        //  "System.Diagnostics.Tracing.EventLevel, System.Private.CoreLib, Version=7.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e"
        //Whitespace doesn't seem to be guaranted, but that's thankfully easy to deal with,
        //since commas only appear inside generic type specs and multi-dim arrays.
        int asmNameIdx = str.IndexOf(',', str.LastIndexOf(']') + 1); //next comma after the last ']' (if there's one)
        if (asmNameIdx > 0) {
            var asmName = new AssemblyName(str[(asmNameIdx + 1)..]);
            str = str[0..asmNameIdx];
            module = _loader._resolver.Resolve(asmName, throwIfNotFound: true);
        }
        int pos = 0;
        return Parse();

        TypeDesc? ResolveType(string name)
        {
            int nsIdx = name.LastIndexOf('.');
            string? typeNs = nsIdx < 0 ? null : name[0..nsIdx];
            string typeName = nsIdx < 0 ? name : name[(nsIdx + 1)..];
            return module.FindType(typeNs, typeName);
        }
        TypeDesc Parse()
        {
            int numGenArgs = 0;
            string name = ScanName(ref numGenArgs);
            var type = ResolveType(name);

            //Nested types
            while (Match('+')) {
                string childName = ScanName(ref numGenArgs);
                type = (type as TypeDef)?.GetNestedType(childName);
            }
            if (type == null) {
                throw Error("Specified type could not be found");
            }
            //Generic arguments
            if (numGenArgs > 0 && Match('[')) {
                var args = ImmutableArray.CreateBuilder<TypeDesc>();
                for (int i = 0; i < numGenArgs; i++) {
                    if (i != 0) Expect(',');
                    args.Add(Parse());
                }
                Expect(']');
                type = ((TypeDef)type).GetSpec(args.TakeImmutable());
            }
            //Compound types (array, pointer, byref)
            while (true) {
                if (Match('[')) {
                    //TODO: multi dim arrays
                    Expect(']');
                    type = type.CreateArray();
                } else if (Match('*')) {
                    type = type.CreatePointer();
                } else if (Match('&')) {
                    type = type.CreateByref();
                } else break;
            }
            return type;
        }
        bool Match(char ch)
        {
            if (pos < str.Length && str[pos] == ch) {
                pos++;
                return true;
            }
            return false;
        }
        void Expect(char ch)
        {
            if (pos >= str.Length || str[pos] != ch) {
                throw Error($"Expected '{ch}'");
            }
        }
        string ScanName(ref int numGenArgs)
        {
            int len = str.AsSpan(pos).IndexOfAny("+[,]&*");
            if (len < 0) {
                len = str.Length - pos;
            } else if (len == 0) {
                throw Error("Expected type name");
            }
            string val = str.Substring(pos, len);
            pos += len;

            int backtickIdx = val.IndexOf('`');
            if (backtickIdx >= 0) {
                numGenArgs += int.Parse(val.AsSpan(backtickIdx + 1));
            }
            return val;
        }
        Exception Error(string msg)
            => new FormatException($"Failed to parse serialized type name: {msg} (for '{str}' at {pos})");
    }
}