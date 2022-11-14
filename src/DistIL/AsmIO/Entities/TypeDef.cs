namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

public abstract class TypeDefOrSpec : TypeDesc, ModuleEntity
{
    public abstract ModuleDef Module { get; }
    public TypeAttributes Attribs { get; set; }

    public override bool IsValueType {
        get {
            var sys = Module.Resolver.SysTypes;
            //System.Enum weirdly extends ValueType, but it's not actually one
            return (BaseType == sys.ValueType && this != sys.Enum) || BaseType == sys.Enum;
        }
    }
    public override bool IsEnum => BaseType == Module.Resolver.SysTypes.Enum;
    public override bool IsInterface => (Attribs & TypeAttributes.Interface) != 0;
    public override bool IsGeneric => GenericParams.Length > 0;

    protected TypeDefOrSpec? _baseType;
    public override TypeDefOrSpec? BaseType => _baseType;

    public abstract IReadOnlyList<TypeDesc> Interfaces { get; }
    public ImmutableArray<TypeDesc> GenericParams { get; protected set; }

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

    /// <summary> The enclosing type of this nested type, or null if not nested. </summary>
    public TypeDef? DeclaringType { get; set; }

    [MemberNotNullWhen(true, nameof(DeclaringType))]
    public bool IsNested => (Attribs & TypeAttributes.NestedFamANDAssem) != 0;

    public override TypeDesc? UnderlyingEnumType => IsEnum ? Fields.First(f => f.IsInstance).Type : null;

    public int LayoutSize { get; set; }
    public int LayoutPack { get; set; }
    public bool HasCustomLayout => LayoutSize != 0 || LayoutPack != 0;

    private List<FieldDef> _fields = new();
    private List<MethodDef> _methods = new();

    private List<TypeDesc>? _interfaces; //Note: CAs are linked to indices, care must be taken when changing entries
    private List<PropertyDef>? _properties;
    private List<EventDef>? _events;
    private List<TypeDef>? _nestedTypes;
    private Dictionary<MethodDesc, MethodDef>? _itfMethodImpls;

    private static readonly Dictionary<MethodDesc, MethodDef> s_EmptyItfMethodImpls = new();
    private static IReadOnlyList<T> EmptyIfNull<T>(List<T>? list) => list ?? (IReadOnlyList<T>)Array.Empty<T>();

    public override IReadOnlyList<TypeDesc> Interfaces => EmptyIfNull(_interfaces);
    public override IReadOnlyList<FieldDef> Fields => _fields;
    public override IReadOnlyList<MethodDef> Methods => _methods;
    public IReadOnlyList<PropertyDef> Properties => EmptyIfNull(_properties);
    public IReadOnlyList<EventDef> Events => EmptyIfNull(_events);
    public IReadOnlyList<TypeDef> NestedTypes => EmptyIfNull(_nestedTypes);
    public IReadOnlyDictionary<MethodDesc, MethodDef> InterfaceMethodImpls => _itfMethodImpls ?? s_EmptyItfMethodImpls;

    internal TypeDef(
        ModuleDef mod, string? ns, string name, 
        TypeAttributes attribs = default,
        ImmutableArray<TypeDesc> genericParams = default,
        TypeDefOrSpec? baseType = null)
    {
        Module = mod;
        Namespace = ns;
        Name = name;
        Attribs = attribs;
        GenericParams = genericParams.EmptyIfDefault();
        _baseType = baseType;
    }

    public override TypeDefOrSpec GetSpec(GenericContext context)
    {
        return IsGeneric
            ? new TypeSpec(this, context.FillParams(GenericParams))
            : this;
    }
    public TypeSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric && genArgs.Length == GenericParams.Length);
        return new TypeSpec(this, genArgs);
    }

    public TypeDef? GetNestedType(string name)
    {
        return _nestedTypes?.Find(e => e.Name == name);
    }

    public MethodDef CreateMethod(
        string name, TypeSig retSig, ImmutableArray<ParamDef> paramSig, 
        MethodAttributes attribs = MethodAttributes.Public | MethodAttributes.HideBySig,
        ImmutableArray<GenericParamType> genericPars = default)
    {
        var existingMethod = FindMethod(name, new MethodSig(retSig, paramSig.Select(p => p.Sig).ToList()));
        Ensure.That(existingMethod == null, "A method with the same signature already exists");

        var method = new MethodDef(
            this, retSig, paramSig, name, attribs, 
            genericParams: genericPars.IsDefault ? default : genericPars.CastArray<TypeDesc>()
        );
        _methods.Add(method);
        return method;
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        if (DeclaringType != null) {
            ctx.Print($"{DeclaringType}+{(IsValueType ? PrintToner.StructName : PrintToner.ClassName)}{Name}");
        } else {
            base.Print(ctx, includeNs);
        }
    }

    internal static TypeDef Decode(ModuleLoader loader, TypeDefinition info)
    {
        return new TypeDef(
            loader._mod,
            loader._reader.GetOptString(info.Namespace),
            loader._reader.GetString(info.Name),
            info.Attributes,
            loader.CreateGenericParams(info.GetGenericParameters(), false)
        );
    }
    internal void Load1(ModuleLoader loader, TypeDefinition info)
    {
        if (!info.BaseType.IsNil) {
            _baseType = (TypeDefOrSpec)loader.GetEntity(info.BaseType);
        }
        if (info.IsNested) {
            DeclaringType = loader.GetType(info.GetDeclaringType());
            (DeclaringType._nestedTypes ??= new()).Add(this);
        }
        var layout = info.GetLayout();
        LayoutPack = layout.PackingSize;
        LayoutSize = layout.Size;
    }
    internal void Load2(ModuleLoader loader, TypeDefinition info)
    {
        var fieldHandles = info.GetFields();
        _fields.EnsureCapacity(fieldHandles.Count);
        foreach (var handle in fieldHandles) {
            _fields.Add(loader.GetField(handle));
        }

        var methodHandles = info.GetMethods();
        _methods.EnsureCapacity(methodHandles.Count);
        foreach (var handle in methodHandles) {
            _methods.Add(loader.GetMethod(handle));
        }

        _kind = IsEnum ? UnderlyingEnumType!.Kind :
                PrimType.GetFromDefinition(this)?.Kind ??
                (IsValueType ? TypeKind.Struct : TypeKind.Object);
    }
    internal void Load3(ModuleLoader loader, TypeDefinition info)
    {
        var propHandles = info.GetProperties();
        if (propHandles.Count > 0) {
            _properties = new List<PropertyDef>(propHandles.Count);

            foreach (var handle in propHandles) {
                _properties.Add(PropertyDef.Decode3(loader, handle, this));
            }
        }

        var evtHandles = info.GetEvents();
        if (evtHandles.Count > 0) {
            _events = new List<EventDef>(evtHandles.Count);

            foreach (var handle in evtHandles) {
                _events.Add(EventDef.Decode3(loader, handle, this));
            }
        }

        var itfHandles = info.GetInterfaceImplementations();
        if (itfHandles.Count > 0) {
            _interfaces = new List<TypeDesc>(itfHandles.Count);

            foreach (var handle in itfHandles) {
                var itfInfo = loader._reader.GetInterfaceImplementation(handle);
                loader.FillCustomAttribs(this, itfInfo.GetCustomAttributes(), CustomAttribLink.Type.InterfaceImpl, _interfaces.Count);
                _interfaces.Add((TypeDesc)loader.GetEntity(itfInfo.Interface));
            }
        }

        var implHandles = info.GetMethodImplementations();
        if (implHandles.Count > 0) {
            _itfMethodImpls = new(implHandles.Count);

            foreach (var handle in implHandles) {
                var implInfo = loader._reader.GetMethodImplementation(handle);
                var decl = (MethodDesc)loader.GetEntity(implInfo.MethodDeclaration);
                var impl = (MethodDef)loader.GetEntity(implInfo.MethodBody);

                Ensure.That(impl.DeclaringType == this);
                _itfMethodImpls.Add(decl, impl);
                loader.FillCustomAttribs(impl, implInfo.GetCustomAttributes(), CustomAttribLink.Type.InterfaceImpl);
            }
        }
        loader.FillGenericParams(this, GenericParams, info.GetGenericParameters());
        loader.FillCustomAttribs(this, info.GetCustomAttributes());
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

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        Definition.Print(ctx, includeNs);
        ctx.PrintSequence("[", "]", GenericParams, v => v.Print(ctx, includeNs));
    }

    public override bool Equals(TypeDesc? other)
        => other is TypeSpec o && o.Definition.Equals(Definition) &&
           o.GenericParams.SequenceEqual(GenericParams);
}
