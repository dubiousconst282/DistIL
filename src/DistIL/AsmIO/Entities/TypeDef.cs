namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

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

    public abstract IReadOnlyList<TypeDesc> Interfaces { get; }
    public ImmutableArray<TypeDesc> GenericParams { get; protected set; }

    /// <summary> The enclosing type of this nested type, or null if not nested. </summary>
    public TypeDefOrSpec? DeclaringType { get; set; }

    [MemberNotNullWhen(true, nameof(IsEnum))]
    public TypeDesc? UnderlyingEnumType => IsEnum ? Fields.First(f => f.IsInstance).Type : null;

    public override bool Equals(TypeDesc? other) => object.ReferenceEquals(this, other);
    public override int GetHashCode() => HashCode.Combine(Module, Name, GenericParams.Length);
}

public class TypeDef : TypeDefOrSpec
{
    public override ModuleDef Module { get; }
    public override TypeKind Kind => _kind;
    public override StackType StackType => _kind.ToStackType();

    private TypeKind _kind;
    public override string? Namespace { get; }
    public override string Name { get; }

    public int LayoutSize { get; set; }
    public int LayoutPack { get; set; }

    private List<TypeDesc> _interfaces = new();
    private List<FieldDef> _fields = new();
    private List<MethodDef> _methods = new();
    private List<PropertyDef> _properties = new();
    private List<EventDef> _events = new();
    private List<TypeDef> _nestedTypes = new();

    public override IReadOnlyList<TypeDesc> Interfaces => _interfaces;
    public override IReadOnlyList<FieldDef> Fields => _fields;
    public override IReadOnlyList<MethodDef> Methods => _methods;
    public IReadOnlyList<PropertyDef> Properties => _properties;
    public IReadOnlyList<EventDef> Events => _events;
    public IReadOnlyList<TypeDef> NestedTypes => _nestedTypes;

    public TypeDef(ModuleDef mod, string? ns, string name, TypeAttributes attribs = default, ImmutableArray<TypeDesc> genericParams = default)
    {
        Module = mod;
        Namespace = ns;
        Name = name;
        Attribs = attribs;
        GenericParams = genericParams.EmptyIfDefault();
    }

    internal void Load1(ModuleLoader loader, TypeDefinition info)
    {
        if (!info.BaseType.IsNil) {
            _baseType = (TypeDefOrSpec)loader.GetEntity(info.BaseType);
        }
        if (info.IsNested) {
            DeclaringType = loader.GetType(info.GetDeclaringType());
        }
        loader.FillGenericParams(GenericParams, info.GetGenericParameters());

        var layout = info.GetLayout();
        LayoutPack = layout.PackingSize;
        LayoutSize = layout.Size;
    }
    internal void Load2(ModuleLoader loader, TypeDefinition info)
    {
        foreach (var handle in info.GetFields()) {
            _fields.Add(loader.GetField(handle));
        }
        foreach (var handle in info.GetMethods()) {
            _methods.Add(loader.GetMethod(handle));
        }
        foreach (var handle in info.GetProperties()) {
            _properties.Add(loader.DecodeProperty(this, handle));
        }
        foreach (var handle in info.GetEvents()) {
            _events.Add(loader.DecodeEvent(this, handle));
        }
        foreach (var handle in info.GetNestedTypes()) {
            _nestedTypes.Add(loader.GetType(handle));
        }

        foreach (var handle in info.GetInterfaceImplementations()) {
            var itfInfo = loader._reader.GetInterfaceImplementation(handle);
            var itf = (TypeDesc)loader.GetEntity(itfInfo.Interface);
            _interfaces.Add(itf);
        }

        //FIXME: TypeDef.Kind for String/Array/... and maybe primitives?
        _kind = IsEnum ? UnderlyingEnumType!.Kind :
               IsValueType ? TypeKind.Struct : 
               TypeKind.Object;
    }

    public override TypeDefOrSpec GetSpec(GenericContext context)
    {
        return IsGeneric 
            ? new TypeSpec(this, context.FillParams(GenericParams)) 
            : this;
    }
    public TypeSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric);
        return new TypeSpec(this, genArgs);
    }

    public TypeDef? GetNestedType(string name)
    {
        return _nestedTypes.Find(e => e.Name == name);
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
        get => Definition.Methods.Select(m => new MethodSpec(this, m)).ToList();
    }
    public override IReadOnlyList<TypeDesc> Interfaces {
        get => Definition.Interfaces.Select(t => t.GetSpec(new GenericContext(this))).ToList();
    }

    internal TypeSpec(TypeDef def, ImmutableArray<TypeDesc> args)
    {
        Definition = def;
        GenericParams = args;
    }

    public override MethodDesc? FindMethod(string name, in MethodSig sig, in GenericContext spec = default)
    {
        var method = Definition.FindMethod(name, sig, spec.IsNull ? new GenericContext(this) : spec);
        return method != null ? new MethodSpec(this, (MethodDef)method) : null;
    }

    public override FieldDesc? FindField(string name)
    {
        var field = Definition.FindField(name);
        return field != null ? new FieldSpec(this, (FieldDef)field) : null;
    }

    public override TypeDesc GetSpec(GenericContext context)
    {
        return new TypeSpec(Definition, context.FillParams(GenericParams));
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
