namespace DistIL.AsmIO;

public static class TypeSystemExt
{
    /// <summary> Checks if this type is declared in <c>System.Private.CoreLib</c>. </summary>
    public static bool IsCorelibType(this TypeDesc desc)
    {
        return desc is TypeDefOrSpec def && def.Module == def.Module.Resolver.CoreLib;
    }
    
    /// <summary> Checks if this type is the definition of a type in CoreLib, <paramref name="rtType"/>. </summary>
    /// <remarks> This method compares by names, generic instantiations are not considered. </remarks>
    public static bool IsCorelibType(this TypeDesc desc, Type rtType)
    {
        return desc.IsCorelibType() &&
               desc.Name == rtType.Name &&
               desc.Namespace == rtType.Namespace;
    }

    /// <summary> Whether this type represents a primitive integer (byte, int, long, etc.). </summary>
    public static bool IsInt(this TypeDesc type)
    {
        return type.StackType is StackType.Int or StackType.Long;
    }
    /// <summary> Whether this type represents a float or double. </summary>
    public static bool IsFloat(this TypeDesc type)
    {
        return type.StackType is StackType.Float;
    }

    /// <summary> Whether this type represents a byref, pointer, or nint. </summary>
    public static bool IsPointerLike(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt;
    }

    /// <summary> Whether this type represents an unmanaged pointer or nint. </summary>
    public static bool IsRawPointer(this TypeDesc type)
    {
        return type.StackType is StackType.NInt;
    }

    public static bool IsPointerOrObject(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt or StackType.Object;
    }

    public static bool IsManagedObject(this TypeDesc type)
    {
        return type.StackType is StackType.Object;
    }

    /// <summary> If this type is generic, returns an <see cref="TypeSpec"/> with all unbound parameters. Otherwise, returns the unchanged instance. </summary>
    public static TypeDesc GetUnboundSpec(this TypeDesc type)
    {
        return type is TypeDefOrSpec { Definition: var def } 
            ? def.GetSpec(new GenericContext(def))
            : type;
    }
}