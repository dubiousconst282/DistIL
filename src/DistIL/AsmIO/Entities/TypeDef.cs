namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Text;

using DistIL.IR;

public abstract class TypeDefOrSpec : TypeDesc, ModuleEntity
{
    public abstract ModuleDef Module { get; }
    public TypeAttributes Attribs { get; set; }

    public override bool IsValueType => BaseType == Module.SysTypes.ValueType;
    public override bool IsEnum => BaseType == Module.SysTypes.Enum;
    public override bool IsInterface => (Attribs & TypeAttributes.Interface) != 0;
    public override bool IsGeneric => GenericParams.Length > 0;

    [MemberNotNullWhen(true, nameof(DeclaringType))]
    public bool IsNested => (Attribs & TypeAttributes.NestedFamANDAssem) != 0;
    
    protected TypeDefOrSpec? _baseType;
    public override TypeDefOrSpec? BaseType => _baseType;

    public List<InterfaceImpl> Interfaces { get; } = new();
    public ImmutableArray<TypeDesc> GenericParams { get; protected set; }

    /// <summary> The enclosing type of this nested type, or null if not nested. </summary>
    public TypeDefOrSpec? DeclaringType { get; set; }

    [MemberNotNullWhen(true, nameof(IsEnum))]
    public TypeDesc? UnderlyingEnumType { get; protected set; }

    public override bool Equals(TypeDesc? other) => object.ReferenceEquals(this, other);
    public override int GetHashCode() => HashCode.Combine(Module, Name, GenericParams.Length);
}
public struct InterfaceImpl
{
    public TypeDesc Interface { get; }
    public ImmutableArray<CustomAttrib> CustomAttribs { get; }

    public InterfaceImpl(TypeDesc itf, ImmutableArray<CustomAttrib> customAttribs)
    {
        Interface = itf;
        CustomAttribs = customAttribs.EmptyIfDefault();
    }
}

public class TypeDef : TypeDefOrSpec
{
    public override ModuleDef Module { get; }
    public override TypeKind Kind => _kind;
    public override StackType StackType => _kind.ToStackType();

    private TypeKind _kind;
    private string? _namespace, _name;
    public override string? Namespace => _namespace;
    public override string Name => _name!;

    public int LayoutSize { get; set; }
    public int LayoutPack { get; set; }

    public override List<FieldDef> Fields { get; } = new();
    public override List<MethodDef> Methods { get; } = new();
    public List<PropertyDef> Properties { get; } = new();
    public List<EventDef> Events { get; } = new();
    public List<TypeDef> NestedTypes { get; } = new();

    public TypeDef(ModuleDef mod, string? ns, string name, TypeAttributes attribs = default, ImmutableArray<TypeDesc> genericParams = default)
    {
        Module = mod;
        _namespace = ns;
        _name = name;
        Attribs = attribs;
        GenericParams = genericParams.EmptyIfDefault();
    }

    internal void Load(ModuleLoader loader, TypeDefinition info)
    {
        if (!info.BaseType.IsNil) {
            _baseType = (TypeDefOrSpec)loader.GetEntity(info.BaseType);
        }
        if (info.IsNested) {
            DeclaringType = loader.GetType(info.GetDeclaringType());
        }
        GenericParams = loader.DecodeGenericParams(info.GetGenericParameters());

        var layout = info.GetLayout();
        LayoutPack = layout.PackingSize;
        LayoutSize = layout.Size;
    }
    internal void Load2(ModuleLoader loader, TypeDefinition info)
    {
        foreach (var handle in info.GetFields()) {
            Fields.Add(loader.GetField(handle));
        }
        foreach (var handle in info.GetMethods()) {
            Methods.Add(loader.GetMethod(handle));
        }
        foreach (var handle in info.GetProperties()) {
            Properties.Add(loader.DecodeProperty(this, handle));
        }
        foreach (var handle in info.GetEvents()) {
            Events.Add(loader.DecodeEvent(this, handle));
        }
        foreach (var handle in info.GetNestedTypes()) {
            NestedTypes.Add(loader.GetType(handle));
        }
        if (IsEnum) {
            UnderlyingEnumType = Fields.First(f => f.IsInstance).Type;
        }

        foreach (var itfHandle in info.GetInterfaceImplementations()) {
            var itfInfo = loader._reader.GetInterfaceImplementation(itfHandle);
            var itf = (TypeDesc)loader.GetEntity(itfInfo.Interface);
            var attribs = loader.DecodeCustomAttribs(itfInfo.GetCustomAttributes());
            Interfaces.Add(new InterfaceImpl(itf, attribs));
        }
        CustomAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());

        //FIXME: TypeDef.Kind for String/Array/... and maybe primitives?
        _kind = IsEnum ? UnderlyingEnumType!.Kind :
               IsValueType ? TypeKind.Struct : 
               TypeKind.Object;
    }

    public override TypeDesc GetSpec(GenericContext context)
    {
        if (!IsGeneric) {
            return this;
        }
        var genArgs = GenericParams
            .Select(p => p.GetSpec(context))
            .ToImmutableArray();
        return new TypeSpec(this, genArgs);
    }
    public TypeSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure(IsGeneric);
        return new TypeSpec(this, genArgs);
    }

    //overriden props can't have setter
    public void SetName(string value) => _name = value;
    public void SetNamespace(string? value) => _namespace = value;
    public void SetBaseType(TypeDef? value) => _baseType = value;

    public TypeDef? GetNestedType(string name)
    {
        foreach (var type in NestedTypes) {
            if (type.Name == name) {
                return (TypeDef)type;
            }
        }
        return null;
    }

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        if (DeclaringType != null) {
            DeclaringType.Print(ctx, includeNs);
            ctx.Print("+");
            ctx.Print(Name, PrintToner.TypeName);
        } else {
            base.Print(ctx, includeNs);
        }
    }
}

/// <summary> Represents a generic type instantiation. </summary>
public class TypeSpec : TypeDefOrSpec
{
    public override ModuleDef Module => Definition.Module;
    /// <summary> The generic type definition. </summary>
    public TypeDef Definition { get; }

    public override TypeKind Kind => Definition.Kind;
    public override StackType StackType => Definition.StackType;
    public override TypeDefOrSpec? BaseType => Definition.BaseType;

    public override string? Namespace => Definition.Namespace;
    public override string Name => Definition.Name;

    //TypeSpec members cannot be changed because they reflect the parent def
    //TODO: find a way to cache TypeSpec members
    public override IReadOnlyList<FieldSpec> Fields {
        get => Definition.Fields.Select(f => new FieldSpec(this, f)).ToList();
    }
    public override IReadOnlyList<MethodSpec> Methods {
        get => Definition.Methods.Select(m => new MethodSpec(this, m)).ToImmutableArray();
    }

    internal TypeSpec(TypeDef def, ImmutableArray<TypeDesc> args)
    {
        Definition = def;
        GenericParams = args;
    }

    public override MethodDesc? FindMethod(string name, in MethodSig sig)
    {
        var method = Definition.FindMethod(name, sig);
        return method != null ? new MethodSpec(this, (MethodDef)method) : null;
    }

    public override FieldDesc? FindField(string name, TypeDesc? type = null)
    {
        var field = Definition.FindField(name, type);
        return field != null ? new FieldSpec(this, (FieldDef)field) : null;
    }

    public override TypeDesc GetSpec(GenericContext context)
    {
        var genArgs = GenericParams
            .Select(p => p.GetSpec(context))
            .ToImmutableArray();
        return new TypeSpec(Definition, genArgs);
    }

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        Definition.Print(ctx, includeNs);
        ctx.PrintSequence("[", "]", GenericParams, v => v.Print(ctx, includeNs));
    }

    public override bool Equals(TypeDesc? other)
        => other is TypeSpec o && o.Definition.Equals(Definition) &&
           o.GenericParams.SequenceEqual(GenericParams);
}
