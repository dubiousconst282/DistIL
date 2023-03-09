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

    /// <summary> Checks whether <paramref name="type"/> is a byref, pointer, or nint. </summary>
    public static bool IsPointerLike(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt;
    }

    /// <summary> Checks whether <paramref name="type"/> is an unmanaged pointer or nint. </summary>
    public static bool IsRawPointer(this TypeDesc type)
    {
        return type.StackType is StackType.NInt;
    }

    /// <summary> If <paramref name="type"/> is a generic type, returns an <see cref="TypeSpec"/> with all unbound parameters. Otherwise, returns <paramref name="type"/> unchanged. </summary>
    public static TypeDesc GetUnboundSpec(this TypeDesc type)
    {
        return type is TypeDefOrSpec { Definition: var def } 
            ? def.GetSpec(new GenericContext(def))
            : type;
    }
}