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

    //Member lists must be lazily initialized to prevent infinite recursion on TypeDef ctor
    private List<FieldDef>? _fields;
    public List<FieldDef> Fields => _fields ??= LoadFields();

    private List<MethodDef>? _methods;
    public List<MethodDef> Methods => _methods ??= LoadMethods();

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

    private List<FieldDef> LoadFields()
    {
        var handles = _entity.GetFields();
        var fields = new List<FieldDef>(handles.Count);
        foreach (var handle in handles) {
            fields.Add(Module.GetField(handle));
        }
        return fields;
    }
    private List<MethodDef> LoadMethods()
    {
        var handles = _entity.GetMethods();
        var methods = new List<MethodDef>(handles.Count);
        foreach (var handle in handles) {
            methods.Add(Module.GetMethod(handle));
        }
        return methods;
    }

    public override bool Equals(RType? other)
        => object.ReferenceEquals(this, other);
}