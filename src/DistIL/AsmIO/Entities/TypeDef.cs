namespace DistIL.AsmIO;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

public abstract class TypeDefOrSpec : TypeDesc, ModuleEntity
{
    public abstract ModuleDef Module { get; }
    public abstract TypeDef Definition { get; }
    public abstract override TypeDefOrSpec? BaseType { get; }

    public abstract TypeAttributes Attribs { get; }

    public override bool IsValueType {
        get {
            var sys = Module.Resolver.SysTypes;
            //System.Enum weirdly extends ValueType, but it's not actually one
            return (BaseType == sys.ValueType && this != sys.Enum) || BaseType == sys.Enum;
        }
    }
    public override bool IsEnum => BaseType == Module.Resolver.SysTypes.Enum;
    public override bool IsInterface => (Attribs & TypeAttributes.Interface) != 0;

    public override abstract TypeDefOrSpec GetSpec(GenericContext context);

    public virtual IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => Definition.GetCustomAttribs(readOnly);
    
    public override int GetHashCode() => HashCode.Combine(Module, Name);
}

public class TypeDef : TypeDefOrSpec
{
    public override ModuleDef Module { get; }
    public override TypeDef Definition => this;

    public override TypeKind Kind => _kind;
    public override StackType StackType => _kind.ToStackType();
    public override TypeAttributes Attribs { get; }

    public override TypeDefOrSpec? BaseType => _baseType;
    public override IReadOnlyList<GenericParamType> GenericParams { get; }

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

    public override IReadOnlyList<TypeDesc> Interfaces => EmptyIfNull(_interfaces);
    public override IReadOnlyList<FieldDef> Fields => _fields;
    public override IReadOnlyList<MethodDef> Methods => _methods;
    public IReadOnlyList<PropertyDef> Properties => EmptyIfNull(_properties);
    public IReadOnlyList<EventDef> Events => EmptyIfNull(_events);
    public IReadOnlyList<TypeDef> NestedTypes => EmptyIfNull(_nestedTypes);
    public IReadOnlyDictionary<MethodDesc, MethodDef> InterfaceMethodImpls => _itfMethodImpls ?? s_EmptyItfMethodImpls;

    private TypeKind _kind;
    private TypeDefOrSpec? _baseType;
    private List<FieldDef> _fields = new();
    private List<MethodDef> _methods = new();

    private List<TypeDesc>? _interfaces;
    private List<PropertyDef>? _properties;
    private List<EventDef>? _events;
    private List<TypeDef>? _nestedTypes;
    private Dictionary<MethodDesc, MethodDef>? _itfMethodImpls;

    private IList<CustomAttrib>? _customAttribs;
    private Dictionary<(Entity, Entity?), IList<CustomAttrib>>? _itfCustomAttribs;

    private SpecCache? _specCache;

    private static readonly Dictionary<MethodDesc, MethodDef> s_EmptyItfMethodImpls = new();
    private static IReadOnlyList<T> EmptyIfNull<T>(List<T>? list) => list ?? (IReadOnlyList<T>)Array.Empty<T>();

    internal TypeDef(
        ModuleDef mod, string? ns, string name, 
        TypeAttributes attribs = default,
        ImmutableArray<GenericParamType> genericParams = default,
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
        return IsGeneric ? GetCachedSpec(GenericParams, context) : this;
    }
    public TypeSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric && genArgs.Length == GenericParams.Count);
        return GetCachedSpec(genArgs, default);
    }
    public TypeSpec GetSpec(TypeDesc genArg1)
    {
        return GetSpec(ImmutableArray.Create(genArg1));
    }

    internal TypeSpec GetCachedSpec(IReadOnlyList<TypeDesc> pars, GenericContext ctx)
    {
        _specCache ??= new();
        ref var spec = ref _specCache.Get(pars, ctx, out var filledArgs);
        return spec ??= new TypeSpec(this, filledArgs);
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
        var existingMethod = FindMethod(name, new MethodSig(retSig, paramSig.Select(p => p.Sig).ToList()), throwIfNotFound: false);
        Ensure.That(existingMethod == null, "A method with the same signature already exists");

        var method = new MethodDef(this, retSig, paramSig, name, attribs, MethodImplAttributes.IL, genericPars);
        _methods.Add(method);
        return method;
    }

    public override IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribExt.GetOrInitList(ref _customAttribs, readOnly);

    /// <summary> Returns the list of custom attributes applied to an interface implementation, <paramref name="interface_"/>. </summary>
    public IList<CustomAttrib> GetCustomAttribs(TypeDesc interface_, bool readOnly = true)
        => GetItfCustomAttribs((interface_, null), readOnly);

    /// <summary> Returns the list of custom attributes applied to an interface method implementation. </summary>
    /// <param name="impl">The method declared in this class for which to override <paramref name="decl"/>. </param>
    /// <param name="decl">The interface method declaration.</param>
    public IList<CustomAttrib> GetCustomAttribs(MethodDef impl, MethodDesc decl, bool readOnly = true)
    {
        Ensure.That(impl.DeclaringType == this);
        return GetItfCustomAttribs((impl, decl), readOnly);
    }

    private IList<CustomAttrib> GetItfCustomAttribs((Entity, Entity?) key, bool readOnly)
    {
        return readOnly
            ? _itfCustomAttribs?.GetValueOrDefault(key) ?? Array.Empty<CustomAttrib>()
            : CustomAttribExt.GetOrInitList(ref (_itfCustomAttribs ??= new()).GetOrAddRef(key), readOnly);
    }

    private void SetItfCustomAttribs((Entity, Entity?) key, IList<CustomAttrib>? list)
    {
        if (list != null) {
            (_itfCustomAttribs ??= new()).Add(key, list);
        }
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
                var itfType = (TypeDesc)loader.GetEntity(itfInfo.Interface);
                _interfaces.Add(itfType);
                SetItfCustomAttribs((itfType, null), loader.DecodeCustomAttribs(itfInfo.GetCustomAttributes()));
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
                SetItfCustomAttribs((impl, decl), loader.DecodeCustomAttribs(implInfo.GetCustomAttributes()));
            }
        }
        loader.FillGenericParams(GenericParams, info.GetGenericParameters());
        _customAttribs = loader.DecodeCustomAttribs(info.GetCustomAttributes());
    }

    class SpecCache
    {
        //TODO: experiment with WeakRefs/ConditionalWeakTable
        readonly Dictionary<SpecKey, TypeSpec> _entries = new();

        public ref TypeSpec? Get(IReadOnlyList<TypeDesc> pars, GenericContext ctx, out ImmutableArray<TypeDesc> filledArgs)
        {
            var key = new SpecKey(pars, ctx);
            ref var slot = ref _entries.GetOrAddRef(key, out bool exists);
            filledArgs = exists ? default : key.GetArgs();
            return ref slot;
        }

        struct SpecKey : IEquatable<SpecKey>
        {
            readonly object _data; //Either<TypeDesc, TypeDesc[]>

            public SpecKey(IReadOnlyList<TypeDesc> pars, GenericContext ctx)
            {
                if (pars.Count == 1) {
                    _data = pars[0].GetSpec(ctx);
                } else {
                    var args = ctx.FillParams(pars);
                    //take the internal array directly to avoid boxing
                    _data = Unsafe.As<ImmutableArray<TypeDesc>, TypeDesc[]>(ref args);
                }
            }

            public ImmutableArray<TypeDesc> GetArgs()
            {
                return _data is TypeDesc[] arr
                    ? Unsafe.As<TypeDesc[], ImmutableArray<TypeDesc>>(ref arr)
                    : ImmutableArray.Create((TypeDesc)_data);
            }

            public bool Equals(SpecKey other)
            {
                if (_data is TypeDesc[] sig) {
                    return other._data is TypeDesc[] otherSig && sig.AsSpan().SequenceEqual(otherSig);
                }
                return _data.Equals(other._data); //TypeDesc
            }

            public override int GetHashCode()
            {
                if (_data is TypeDesc[] sig) {
                    var hash = new HashCode();
                    foreach (var type in sig) {
                        hash.Add(type);
                    }
                    return hash.ToHashCode();
                }
                return _data.GetHashCode();
            }

            public override bool Equals(object? obj)
                => throw new InvalidOperationException();
        }
    }
}

/// <summary> Represents a generic type instantiation. </summary>
public class TypeSpec : TypeDefOrSpec
{
    public override ModuleDef Module => Definition.Module;
    public override TypeDef Definition { get; }

    public override TypeKind Kind => Definition.Kind;
    public override StackType StackType => Definition.StackType;
    public override TypeAttributes Attribs => Definition.Attribs;

    public override TypeDefOrSpec? BaseType => Definition.BaseType;
    public override IReadOnlyList<TypeDesc> GenericParams { get; }

    public override string? Namespace => Definition.Namespace;
    public override string Name => Definition.Name;

    public override IReadOnlyList<FieldSpec> Fields => _fields ??= new(Definition.Fields, def => new FieldSpec(this, def));
    public override IReadOnlyList<MethodSpec> Methods => _methods ??= new(Definition.Methods, def => new MethodSpec(this, def));
    public override IReadOnlyList<TypeDesc> Interfaces => _interfaces ??= new(Definition.Interfaces, def => def.GetSpec(new GenericContext(this)));

    MemberList<FieldDef, FieldSpec>? _fields;
    MemberList<MethodDef, MethodSpec>? _methods;
    MemberList<TypeDesc, TypeDesc>? _interfaces;

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
        var memberList = (MemberList<MethodDef, MethodSpec>)Methods;
        return memberList.GetMapping((MethodDef)method);
    }

    public override FieldDesc? FindField(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        var field = Definition.FindField(name, throwIfNotFound);
        var memberList = (MemberList<FieldDef, FieldSpec>)Fields;

        return field == null ? null : memberList.GetMapping((FieldDef)field);
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
