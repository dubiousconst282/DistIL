namespace DistIL.AsmIO;

public class SystemTypes
{
    public readonly TypeDef
        Void,
        Boolean,
        Char,
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        IntPtr,
        UIntPtr,
        String,
        Object,

        ValueType,
        TypedReference,
        Enum,

        Array,
        Type,
        Delegate,

        RuntimeTypeHandle,
        RuntimeFieldHandle,
        RuntimeMethodHandle;

#pragma warning disable CS8618
    internal SystemTypes(ModuleDef coreLib)
    {
        foreach (var field in typeof(SystemTypes).GetFields()) {
            if (field.FieldType != typeof(TypeDef)) continue;

            var typeDef = coreLib.FindType("System", field.Name, throwIfNotFound: true);
            field.SetValue(this, typeDef);
        }
    }
    
    public TypeDef GetPrimitiveDef(TypeKind kind)
    {
        return kind switch {
#pragma warning disable format
            TypeKind.Void       => Void,
            TypeKind.Bool       => Boolean,
            TypeKind.Char       => Char,
            TypeKind.SByte      => SByte,
            TypeKind.Byte       => Byte,
            TypeKind.Int16      => Int16,
            TypeKind.UInt16     => UInt16,
            TypeKind.Int32      => Int32,
            TypeKind.UInt32     => UInt32,
            TypeKind.Int64      => Int64,
            TypeKind.UInt64     => UInt64,
            TypeKind.Single     => Single,
            TypeKind.Double     => Double,
            TypeKind.IntPtr     => IntPtr,
            TypeKind.UIntPtr    => UIntPtr,
            TypeKind.TypedRef   => TypedReference,
            TypeKind.String     => String,
            TypeKind.Object     => Object,
            TypeKind.Array      => Array
#pragma warning restore format
        };
    }
}