namespace DistIL.AsmIO;

using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;

/// <summary> Base class for all method entities. </summary>
public abstract class MethodDesc : MemberDesc
{
    public abstract MethodAttributes Attribs { get; }
    public abstract MethodImplAttributes ImplAttribs { get; }

    public abstract TypeSig ReturnSig { get; }
    public abstract IReadOnlyList<TypeSig> ParamSig { get; }
    public abstract IReadOnlyList<TypeDesc> GenericParams { get; }

    public bool IsStatic => (Attribs & MethodAttributes.Static) != 0;
    public bool IsInstance => !IsStatic;
    public bool IsGeneric => GenericParams.Count > 0;
    public bool IsPublic => (Attribs & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

    public bool IsConstructor => IsSpecialMethod(".ctor");
    public bool IsStaticConstructor => IsSpecialMethod(".cctor");

    public bool IsSpecialName => Attribs.HasFlag(MethodAttributes.SpecialName);

    public bool IsDestructor => Name == "Finalize";

    public TypeDesc ReturnType => ReturnSig.Type;

    private bool IsSpecialMethod(string name)
    {
        return Name == name && Attribs.HasFlag(MethodAttributes.SpecialName);
    }

    public override void Print(PrintContext ctx)
    {
        if (IsStatic) ctx.Print("static ", PrintToner.Keyword);
        ReturnSig.Print(ctx);
        ctx.Print($" {DeclaringType}::{PrintToner.MethodName}{Name}");
        if (IsGeneric) {
            ctx.PrintSequence("<", ">", GenericParams, ctx.Print);
        }
        ctx.PrintSequence("(", ")", ParamSig, p => p.Print(ctx));
    }

    /// <summary> Binds generic type parameters using the given context. </summary>
    /// <remarks>
    /// If neither the method nor its declaring type are generic, or if the
    /// context is empty, the current instance may be returned unchanged. 
    /// </remarks>
    public abstract MethodDesc GetSpec(GenericContext ctx);

    /// <summary> Creates a new generic method specialization with the given type arguments. </summary>
    /// <exception cref="InvalidOperationException"> If this is not a generic method. </exception>
    public virtual MethodDesc GetSpec(ImmutableArray<TypeDesc> genArgs) => throw new InvalidOperationException();
}
public abstract class MethodDefOrSpec : MethodDesc, ModuleEntity
{
    /// <summary> Returns the parent definition if this is a MethodSpec, or the current instance if already a MethodDef. </summary>
    public abstract MethodDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }

    public virtual IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => Definition.GetCustomAttribs(readOnly);

    public override MethodDefOrSpec GetSpec(GenericContext ctx)
    {
        if (!IsGeneric && !DeclaringType.IsGeneric) return this;
        
        var newParent = DeclaringType.GetSpec(ctx);
        bool hasChangedParams = ctx.TryFillParams(GenericParams, out var genArgs);

        if (hasChangedParams || newParent != DeclaringType) {
            if (!hasChangedParams && this is not MethodSpec { IsBoundGeneric: true }) {
                // Return cached spec for unbound spec
                return ((TypeSpec)newParent).GetMapping(Definition);
            }
            return new MethodSpec(newParent, Definition, genArgs);
        }
        return this;
    }
    public override MethodSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric && genArgs.Length == GenericParams.Count);
        return new MethodSpec(DeclaringType, Definition, genArgs);
    }
}
public class MethodDef : MethodDefOrSpec
{
    public override MethodDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override string Name { get; set; }

    public override MethodAttributes Attribs { get; }
    public override MethodImplAttributes ImplAttribs { get; }

    public override TypeSig ReturnSig => ReturnParam.Sig;

    private ParamSigProxyList? _paramSig;
    public override IReadOnlyList<TypeSig> ParamSig => _paramSig ??= new() { Method = this };
    public override GenericParamType[] GenericParams { get; }

    /// <summary> Placeholder for the <c>return</c> parameter, this contains the signature and custom attributes. </summary>
    public ParamDef ReturnParam { get; }

    public ImmutableArray<ParamDef> Params { get; }

    /// <summary> Returns a view over <see cref="Params"/>, excluding the instance parameter if this is not a static method. </summary>
    public ReadOnlySpan<ParamDef> StaticParams => Params.AsSpan(IsStatic ? 0 : 1);

    public ImportDesc? ImportInfo { get; set; }

    private ILMethodBody? _ilBody;
    public ILMethodBody? ILBody {
        get => _ilBody ??= (_bodyRva != 0 ? new ILMethodBody(Module._loader!, _bodyRva) : null);
        set => _ilBody = value;
    }
    public IR.MethodBody? Body { get; set; }

    internal int _bodyRva;
    internal MethodDefinitionHandle _handle;
    internal IList<CustomAttrib>? _customAttribs;

    public MethodDef(
        TypeDef declaringType,
        TypeSig retSig, ImmutableArray<ParamDef> pars, string name,
        MethodAttributes attribs = default, MethodImplAttributes implAttribs = default,
        GenericParamType[]? genericParams = null)
    {
        DeclaringType = declaringType;
        ReturnParam = new ParamDef(retSig, "", ParameterAttributes.Retval);
        Params = pars;
        Name = name;
        Attribs = attribs;
        ImplAttribs = implAttribs;
        GenericParams = genericParams ?? [];

        Ensure.That(
            IsStatic || !declaringType.IsGeneric || pars[0].Type is TypeSpec or ByrefType { ElemType: TypeSpec },
            "`this` parameter for generic type must be specialized with the default parameters");
    }

    public override IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    public MethodDebugSymbols? GetDebugSymbols() => Module.GetDebugSymbols()?.GetMethodSymbols(this);

    class ParamSigProxyList : IReadOnlyList<TypeSig>
    {
        public MethodDef Method = null!;

        public TypeSig this[int index] => Method.Params[index].Sig;
        public int Count => Method.Params.Length;

        public IEnumerator<TypeSig> GetEnumerator() => Method.Params.Select(p => p.Sig).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary> Represents a generic method instantiation. </summary>
public class MethodSpec : MethodDefOrSpec
{
    public override MethodDef Definition { get; }
    public override TypeDefOrSpec DeclaringType { get; }
    public override string Name {
        get => Definition.Name;
        set => throw new InvalidOperationException();
    }

    public override MethodAttributes Attribs => Definition.Attribs;
    public override MethodImplAttributes ImplAttribs => Definition.ImplAttribs;

    public override TypeSig ReturnSig { get; }
    public override IReadOnlyList<TypeSig> ParamSig { get; }
    public override IReadOnlyList<TypeDesc> GenericParams { get; }

    /// <summary> Whether the generic parameters from this spec are different from its open-form definition. </summary>
    public bool IsBoundGeneric => IsGeneric && GenericParams != Definition.GenericParams;

    internal MethodSpec(TypeDefOrSpec declaringType, MethodDef def, IReadOnlyList<TypeDesc> genArgs)
    {
        Debug.Assert(!def.IsGeneric || genArgs.Count > 0);
        
        Definition = def;
        DeclaringType = declaringType;
        GenericParams = genArgs;

        var ctx = new GenericContext(this);
        ReturnSig = def.ReturnSig.GetSpec(ctx);
        ParamSig = GetParamsSpec(ctx);
    }

    private TypeSig[] GetParamsSpec(GenericContext genCtx)
    {
        if (Definition.Params.Length == 0) {
            return [];
        }
        var types = new TypeSig[Definition.Params.Length];
        int index = 0;

        if (Definition.IsInstance) {
            types[index++] = DeclaringType.IsValueType ? DeclaringType.CreateByref() : DeclaringType;
        }
        foreach (var par in Definition.StaticParams) {
            types[index++] = par.Sig.GetSpec(genCtx);
        }
        return types;
    }
}

public class ParamDef
{
    public TypeSig Sig { get; set; }
    public string Name { get; set; }
    public ParameterAttributes Attribs { get; set; }
    public object? DefaultValue { get; set; }
    public byte[]? MarshallingDesc { get; set; }

    public TypeDesc Type => Sig.Type;

    internal IList<CustomAttrib>? _customAttribs;

    public ParamDef(TypeSig sig, string name, ParameterAttributes attribs = default)
    {
        Sig = sig;
        Name = name;
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    public override string ToString() => Sig.ToString();
}

/// <summary> Describes a method's DllImport. </summary>
public class ImportDesc
{
    public string ModuleName { get; set; }
    public string FunctionName { get; set; }
    public MethodImportAttributes Attribs { get; set; }

    public ImportDesc(string modName, string funcName, MethodImportAttributes attribs = 0)
    {
        ModuleName = modName;
        FunctionName = funcName;
        Attribs = attribs;
    }
}