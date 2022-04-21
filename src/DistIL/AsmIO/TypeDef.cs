namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

using DistIL.IR;

public class TypeDef : RType, EntityDef
{
    public ModuleDef Module { get; }
    public EntityHandle Handle { get; }
    private TypeDefinition _entity => Module.Reader.GetTypeDefinition((TypeDefinitionHandle)Handle);

    public override TypeKind Kind { get; }
    public override StackType StackType => IsValueType ? StackType.Struct : StackType.Object;
    public override bool IsValueType { get; }
    public override bool IsGeneric { get; }

    public TypeAttributes Attribs { get; }

    private RType? _baseType;
    /// <summary> Base type of this type. Only null if this is the root type (System.Object). </summary>
    public RType? BaseType => _baseType ??= LoadBaseType();

    /// <summary> The enclosing type of this nested type, or null if not nested. </summary>
    public TypeDef? DeclaringType { get; }

    [MemberNotNullWhen(true, nameof(DeclaringType))]
    public bool IsNested => (Attribs & TypeAttributes.NestedFamANDAssem) != 0;

    public override string? Namespace { get; }
    public override string Name { get; }

    //Member lists must be lazily initialized to prevent infinite recursion on TypeDef ctor
    private List<FieldDef>? _fields;
    public List<FieldDef> Fields => _fields ??= LoadFields();

    private List<MethodDef>? _methods;
    public List<MethodDef> Methods => _methods ??= LoadMethods();

    private List<TypeDef>? _nestedTypes;
    public List<TypeDef> NestedTypes => _nestedTypes ??= LoadNestedTypes();

    public TypeLayout Layout => _entity.GetLayout();

    internal TypeDef(ModuleDef mod, TypeDefinitionHandle handle)
    {
        Module = mod;
        Handle = handle;

        var reader = mod.Reader;
        var entity = reader.GetTypeDefinition(handle);

        Attribs = entity.Attributes;

        Namespace = reader.GetOptString(entity.Namespace);
        Name = reader.GetString(entity.Name);

        if (entity.IsNested) {
            DeclaringType = mod.GetType(entity.GetDeclaringType());
        }
        IsValueType = false; //TODO: resolve value type
        Kind = TypeKind.Object;
    }

    private RType? LoadBaseType()
    {
        if (!_entity.BaseType.IsNil) {
            return Module.GetType(_entity.BaseType);
        }
        return null;
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
    private List<TypeDef> LoadNestedTypes()
    {
        var handles = _entity.GetNestedTypes();
        var types = new List<TypeDef>(handles.Length);
        foreach (var handle in handles) {
            types.Add(Module.GetType(handle));
        }
        return types;
    }

    public TypeDef? GetNestedType(string name)
    {
        foreach (var type in NestedTypes) {
            if (type.Name == name) {
                return type;
            }
        }
        return null;
    }

    public override void Print(StringBuilder sb)
    {
        if (DeclaringType != null) {
            DeclaringType.Print(sb);
            sb.Append("+");
            sb.Append(Name);
        } else {
            base.Print(sb);
        }
    }

    public override bool Equals(RType? other)
        => object.ReferenceEquals(this, other);
}