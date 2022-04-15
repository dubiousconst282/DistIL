namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

public class TypeDef : RType, EntityDef
{
    public ModuleDef Module { get; }
    readonly TypeDefinition _entity;

    public override TypeKind Kind { get; }
    public override StackType StackType => IsValueType ? StackType.Struct : StackType.Object;
    public override bool IsValueType { get; }
    public override bool IsGeneric { get; }

    public TypeAttributes Attribs { get; }

    /// <summary> Base type of this type. Only null if this is the root type (System.Object). </summary>
    public RType? BaseType { get; }

    public override string? Namespace { get; }
    public override string Name { get; }

    internal TypeDef(ModuleDef mod, TypeDefinitionHandle handle)
    {
        Module = mod;
        var reader = mod.Reader;
        _entity = reader.GetTypeDefinition(handle);

        Attribs = _entity.Attributes;

        Namespace = reader.GetString(_entity.Namespace);
        Name = reader.GetString(_entity.Name);

        if (!_entity.BaseType.IsNil) {
            BaseType = mod.GetType(_entity.BaseType);
        }
        IsValueType = false; //TODO: resolve value type
        Kind = TypeKind.Object;
    }

    public IEnumerable<MethodDef> GetMethods()
    {
        foreach (var handle in _entity.GetMethods()) {
            yield return Module.GetMethod(handle);
        }
    }

    public IEnumerable<FieldDef> GetFields()
    {
        foreach (var handle in _entity.GetFields()) {
            yield return Module.GetField(handle);
        }
    }

    public override bool Equals(RType? other)
        => object.ReferenceEquals(this, other);
}