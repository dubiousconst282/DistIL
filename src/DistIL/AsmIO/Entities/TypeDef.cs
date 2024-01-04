namespace DistIL.AsmIO;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;

public abstract class TypeDefOrSpec : TypeDesc, ModuleEntity
{
    public abstract ModuleDef Module { get; }
    public abstract TypeDef Definition { get; }
    public abstract override TypeDefOrSpec? BaseType { get; }

    public abstract TypeAttributes Attribs { get; }

    public override bool IsValueType {
        get {
            var sys = Module.Resolver.SysTypes;
            // System.Enum weirdly extends ValueType, but it's not actually one
            return (BaseType == sys.ValueType && this != sys.Enum) || BaseType == sys.Enum;
        }
    }
    public override bool IsInterface => (Attribs & TypeAttributes.Interface) != 0;
    public override bool IsClass => !IsInterface;

    public override abstract TypeDefOrSpec GetSpec(GenericContext context);

    public TypeSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric && genArgs.Length == GenericParams.Count);
        return Definition.GetCachedSpec(genArgs, default);
    }

    public virtual IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => Definition.GetCustomAttribs(readOnly);
    
    public override int GetHashCode() => HashCode.Combine(Module, Name);
}

public class TypeDef : TypeDefOrSpec
{
    public override ModuleDef Module { get; }
    public override TypeDef Definition => this;

    TypeKind _kind = (TypeKind)(-1);
    public override TypeKind Kind {
        get {
            if (_kind == (TypeKind)(-1)) {
                _kind = IsEnum ? UnderlyingEnumType.Kind :
                        PrimType.GetFromDefinition(this)?.Kind ??
                        (IsValueType ? TypeKind.Struct : TypeKind.Object);
            }
            return _kind;
        }
    }
    public override StackType StackType => _kind.ToStackType();
    public override TypeAttributes Attribs { get; }

    private TypeDefOrSpec? _baseType;
    public override TypeDefOrSpec? BaseType => _baseType ??= _loader?.GetBaseType(_handle);

    GenericParamType[]? _genericParams;
    public override GenericParamType[] GenericParams {
        get {
            if (_genericParams == null) {
                _loader?.LoadGenericParams(_handle, out _genericParams);
                _genericParams ??= [];
            }
            return _genericParams;
        }
        // set => _genericParams = value;
    }
    public override bool IsUnboundGeneric => IsGeneric;

    public override string? Namespace { get; }
    public override string Name { get; }

    /// <summary> The enclosing type of this nested type, or null if not nested. </summary>
    public TypeDef? DeclaringType { get; private set; }

    [MemberNotNullWhen(true, nameof(DeclaringType))]
    public bool IsNested => (Attribs & TypeAttributes.NestedFamANDAssem) != 0;

    public override TypeDesc? UnderlyingEnumType => IsEnum ? Fields.First(f => f.IsInstance).Type : null;

    [MemberNotNullWhen(true, nameof(UnderlyingEnumType))]
    public override bool IsEnum => BaseType == Module.Resolver.SysTypes.Enum;

    public int LayoutSize { get; set; }
    public int LayoutPack { get; set; }
    public bool HasCustomLayout => LayoutSize > 0 || LayoutPack > 0;

    List<FieldDef>? _fields;
    public override List<FieldDef> Fields => _fields ??= (_loader?.LoadFields(this) ?? new());

    List<MethodDef>? _methods;
    public override List<MethodDef> Methods => _methods ??= (_loader?.LoadMethods(this) ?? new());
    
    List<PropertyDef>? _properties;
    public List<PropertyDef> Properties => _properties ??= (_loader?.LoadProperties(this) ?? new());

    List<EventDef>? _events;
    public List<EventDef> Events => _events ??= (_loader?.LoadEvents(this) ?? new());

    public List<TypeDef> NestedTypes { get; } = new();

    List<TypeDesc>? _interfaces;
    public override List<TypeDesc> Interfaces => _interfaces ??= (_loader?.LoadInterfaces(this) ?? new());

    Dictionary<MethodDesc, MethodDef>? _methodImpls;
    public Dictionary<MethodDesc, MethodDef> MethodImpls => _methodImpls ??= (_loader?.LoadMethodImpls(this) ?? new());

    internal IList<CustomAttrib>? _customAttribs;

    // Custom attributes for interfaces and methodImpls, where key is:
    // - Interface: (TypeDef, null) 
    // - MethodImpl: (MethodDesc Decl, MethodDef Impl)
    Dictionary<(EntityDesc, EntityDesc?), IList<CustomAttrib>>? _implCustomAttribs;

    private TypeSpecCache? _specCache;
    internal TypeDefinitionHandle _handle;
    private ModuleLoader? _loader => _handle.IsNil ? null : Module._loader;

    internal TypeDef(
        ModuleDef mod, string? ns, string name,
        TypeAttributes attribs = default,
        GenericParamType[]? genericParams = null,
        TypeDefOrSpec? baseType = null)
    {
        Module = mod;
        Namespace = ns;
        Name = name;
        Attribs = attribs;
        _genericParams = genericParams;
        _baseType = baseType;
    }
    
    public override TypeDefOrSpec GetSpec(GenericContext context)
    {
        return IsGeneric ? GetCachedSpec(GenericParams, context) : this;
    }

    internal TypeSpec GetCachedSpec(IReadOnlyList<TypeDesc> pars, GenericContext ctx)
    {
        _specCache ??= new();
        ref var spec = ref _specCache.Get(pars, ctx, out var filledArgs);
        return spec ??= new TypeSpec(this, filledArgs);
    }

    public TypeDef? FindNestedType(string name)
    {
        return NestedTypes.Find(e => e.Name == name);
    }

    public MethodDef CreateMethod(
        string name, TypeSig retSig, ImmutableArray<ParamDef> paramSig, 
        MethodAttributes attribs = MethodAttributes.Public | MethodAttributes.HideBySig,
        GenericParamType[]? genericPars = null)
    {
        var existingMethod = FindMethod(name, new MethodSig(retSig, paramSig.Select(p => p.Sig).ToList()), throwIfNotFound: false);
        Ensure.That(existingMethod == null, "A method with the same signature already exists");

        var method = new MethodDef(this, retSig, paramSig, name, attribs, MethodImplAttributes.IL, genericPars);
        Methods.Add(method);
        return method;
    }

    public FieldDef CreateField(
        string name, TypeSig sig, FieldAttributes attribs = FieldAttributes.Public,
        object? defaultValue = null, int layoutOffset = -1, byte[]? mappedData = null)
    {
        var existingField = FindField(name, throwIfNotFound: false);
        Ensure.That(existingField == null, "A field with the same name already exists");

        var field = new FieldDef(this, sig, name, attribs, defaultValue, layoutOffset, mappedData);
        Fields.Add(field);
        return field;
    }

    public TypeDef CreateNestedType(
        string name, TypeAttributes attribs = TypeAttributes.Public,
        GenericParamType[]? genericParams = null,
        TypeDefOrSpec? baseType = null)
    {
        var existingType = FindNestedType(name);
        Ensure.That(existingType == null, "A nested type with the same name already exists");

        var newAccess = (attribs & TypeAttributes.VisibilityMask) switch {
            TypeAttributes.NotPublic => TypeAttributes.NestedPrivate,
            TypeAttributes.Public => TypeAttributes.NestedPublic,
            _ => attribs
        };
        attribs = (attribs & ~TypeAttributes.VisibilityMask) | newAccess;

        var childType = new TypeDef(Module, null, name, attribs, genericParams ?? [], baseType);
        childType.SetDeclaringType(this);
        Module._typeDefs.Add(childType);
        return childType;
    }

    public override IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    /// <summary> Returns the list of custom attributes applied to an interface implementation, <paramref name="interfaceType"/>. </summary>
    public IList<CustomAttrib> GetCustomAttribs(TypeDesc interfaceType, bool readOnly = true)
        => GetImplCustomAttribs((interfaceType, null), readOnly);

    /// <summary> Returns the list of custom attributes applied to an interface method implementation. </summary>
    /// <param name="impl">The method declared in this class for which to override <paramref name="decl"/>. </param>
    /// <param name="decl">The interface method declaration.</param>
    public IList<CustomAttrib> GetCustomAttribs(MethodDesc decl, MethodDef impl, bool readOnly = true)
    {
        Ensure.That(impl.DeclaringType == this);
        return GetImplCustomAttribs((decl, impl), readOnly);
    }

    private IList<CustomAttrib> GetImplCustomAttribs((EntityDesc, EntityDesc?) key, bool readOnly)
    {
        return readOnly
            ? _implCustomAttribs?.GetValueOrDefault(key) ?? Array.Empty<CustomAttrib>()
            : CustomAttribUtils.GetOrInitList(ref (_implCustomAttribs ??= new()).GetOrAddRef(key), readOnly);
    }

    internal void SetImplCustomAttribs((EntityDesc, EntityDesc?) key, IList<CustomAttrib>? list)
    {
        if (list != null) {
            (_implCustomAttribs ??= new()).Add(key, list);
        }
    }

    internal void SetDeclaringType(TypeDef parent)
    {
        DeclaringType = parent;
        parent.NestedTypes.Add(this);
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        if (DeclaringType != null) {
            ctx.Print($"{DeclaringType}+{(IsValueType ? PrintToner.StructName : PrintToner.ClassName)}{Name}");
        } else {
            base.Print(ctx, includeNs);
        }
    }

    public override bool Equals(TypeDesc? other)
        => object.ReferenceEquals(this, other) || (other is PrimType p && p.IsDefinition(this));
}

/// <summary> Represents a generic type instantiation. </summary>
public class TypeSpec : TypeDefOrSpec
{
    public override ModuleDef Module => Definition.Module;
    public override TypeDef Definition { get; }

    public override TypeKind Kind => Definition.Kind;
    public override StackType StackType => Definition.StackType;
    public override TypeAttributes Attribs => Definition.Attribs;

    public override TypeDefOrSpec? BaseType {
        get {
            if (_baseType == null || _baseType.Definition != Definition.BaseType) {
                _baseType = Definition.BaseType?.GetSpec(new GenericContext(this));
            }
            return _baseType;
        }
    }
    public override IReadOnlyList<TypeDesc> GenericParams { get; }
    public override bool IsUnboundGeneric => GenericParams.Any(p => p.IsUnboundGeneric);

    public override string? Namespace => Definition.Namespace;
    public override string Name => Definition.Name;

    public override IReadOnlyList<FieldSpec> Fields => _fields ??= new(Definition.Fields, def => new FieldSpec(this, def));
    public override IReadOnlyList<MethodSpec> Methods => _methods ??= new(Definition.Methods, def => new MethodSpec(this, def, def.GenericParams));
    public override IReadOnlyList<TypeDesc> Interfaces => _interfaces ??= new(Definition.Interfaces, def => def.GetSpec(new GenericContext(this)));

    MemberList<FieldDef, FieldSpec>? _fields;
    MemberList<MethodDef, MethodSpec>? _methods;
    MemberList<TypeDesc, TypeDesc>? _interfaces;
    TypeDefOrSpec? _baseType;

    internal TypeSpec(TypeDef def, ImmutableArray<TypeDesc> args)
    {
        Definition = def;
        GenericParams = args;
    }

    public override MethodDesc? FindMethod(
        string name, in MethodSig sig = default, 
        bool searchBaseAndItfs = false, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        var method = Definition.FindMethod(name, sig, searchBaseAndItfs, throwIfNotFound);

        if (method == null) {
            return null;
        }
        if (method.DeclaringType != Definition) {
            return method.GetSpec(new GenericContext(this));
        }
        return GetMapping((MethodDef)method);
    }

    public override FieldDesc? FindField(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        var field = Definition.FindField(name, throwIfNotFound);
        return field == null ? null : GetMapping((FieldDef)field);
    }

    internal MethodSpec GetMapping(MethodDef def)
    {
        var memberList = (MemberList<MethodDef, MethodSpec>)Methods;
        return memberList.GetMapping(def);
    }
    internal FieldSpec GetMapping(FieldDef def)
    {
        var memberList = (MemberList<FieldDef, FieldSpec>)Fields;
        return memberList.GetMapping(def);
    }

    public override TypeSpec GetSpec(GenericContext context)
    {
        return Definition.GetCachedSpec(GenericParams, context);
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        Definition.Print(ctx, includeNs);
        ctx.PrintSequence("[", "]", GenericParams, v => v.Print(ctx, includeNs));
    }

    public override bool Equals(TypeDesc? other)
        => other is TypeSpec o && o.Definition == Definition &&
           o.GenericParams.SequenceEqual(GenericParams);

    class MemberList<TDef, TSpec> : IReadOnlyList<TSpec> where TDef : EntityDesc
    {
        readonly IReadOnlyList<TDef> _source;
        readonly Func<TDef, TSpec> _specFactory;
        Dictionary<TDef, TSpec>? _mappings;

        public TSpec this[int index] => GetMapping(_source[index]);
        public int Count => _source.Count;

        public MemberList(IReadOnlyList<TDef> defs, Func<TDef, TSpec> getSpec)
        {
            _source = defs;
            _specFactory = getSpec;
        }

        public TSpec GetMapping(TDef def)
        {
            _mappings ??= new(ReferenceEqualityComparer.Instance);
            return _mappings.GetOrAddRef(def) ??= _specFactory.Invoke(def);
        }

        public IEnumerator<TSpec> GetEnumerator() => _source.Select(GetMapping).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
