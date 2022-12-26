namespace DistIL.AsmIO;

public static class TypeSystemExt
{
    /// <summary> Checks if this type is declared in `System.Private.CoreLib`. </summary>
    public static bool IsCorelibType(this TypeDesc desc)
    {
        return desc is TypeDefOrSpec def && def.Module == def.Module.Resolver.CoreLib;
    }
    
    /// <summary> Checks if this type is the a definition of a type defined in CoreLib, `rtType`. </summary>
    /// <remarks> This method compares by names, generic instantiations are not considered. </remarks>
    public static bool IsCorelibType(this TypeDesc desc, Type rtType)
    {
        return desc.IsCorelibType() &&
               desc.Name == rtType.Name &&
               desc.Namespace == rtType.Namespace;
    }
}