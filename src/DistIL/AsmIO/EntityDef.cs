namespace DistIL.AsmIO;

using System.Reflection.Metadata;

public interface EntityDef
{
    ModuleDef Module { get; }
    EntityHandle Handle { get; } //TODO: Maybe replace with ModuleDef.GetHandle()
}

public interface MemberDef : EntityDef
{
    TypeDef DeclaringType { get; }
    string Name { get; }
}

public class ExportedType : EntityDef
{
    public ModuleDef Module { get; }
    public EntityHandle Handle { get; }
    /// <summary> The entity declaring the type implementation. Either a ModuleDef, or TypeDef if impl is a nested type. </summary>
    public EntityDef Scope { get; }
    public TypeDef Implementation { get; }

    public ExportedType(ModuleDef mod, EntityHandle handle, EntityDef scope, TypeDef impl)
    {
        Module = mod;
        Handle = handle;
        Scope = scope;
        Implementation = impl;
    }

    public override string ToString() => "-> " + Implementation;
}