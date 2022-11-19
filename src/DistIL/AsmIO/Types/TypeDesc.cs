namespace DistIL.AsmIO;

/// <summary> The base class of all types. </summary>
public abstract class TypeDesc : EntityDesc, IEquatable<TypeDesc>
{
    public abstract TypeKind Kind { get; }
    public abstract StackType StackType { get; }

    public abstract string? Namespace { get; }

    public abstract TypeDesc? BaseType { get; }
    public virtual IReadOnlyList<TypeDesc> Interfaces => Array.Empty<TypeDesc>();
    public virtual ImmutableArray<TypeDesc> GenericParams { get; }

    /// <summary> Element type of the array, pointer or byref type. </summary>
    public virtual TypeDesc? ElemType => null;
    
    public virtual bool IsValueType => false;
    public virtual bool IsEnum => false;
    public virtual bool IsInterface => false;
    public bool IsGeneric => GenericParams.Length > 0;

    [MemberNotNullWhen(true, nameof(IsEnum))]
    public virtual TypeDesc? UnderlyingEnumType => null;

    static readonly List<MethodDesc> s_EmptyMethodList = new();
    static readonly List<FieldDesc> s_EmptyFieldList = new();
    public virtual IReadOnlyList<MethodDesc> Methods { get; } = s_EmptyMethodList;
    public virtual IReadOnlyList<FieldDesc> Fields { get; } = s_EmptyFieldList;

    /// <summary> Checks whether this type can be assigned to a variable of given type, assuming they are values on the evaluation stack. </summary>
    public bool IsStackAssignableTo(TypeDesc assigneeType)
    {
        var t1 = StackType;
        var t2 = assigneeType.StackType;
        if (t1 == t2) {
            return true;
        }
        //Allow implicit conversions: nint/pointer <-> byref, int -> nint
        return (t1 == StackType.NInt && t2 == StackType.ByRef) ||
               (t1 == StackType.ByRef && t2 == StackType.NInt) ||
               (t1 == StackType.Int && t2 == StackType.NInt);
    }

    /// <summary>
    /// Creates a generic type instantiation with the given context as arguments, 
    /// or returns the current instance if it is not a generic type definition.
    /// </summary>
    public virtual TypeDesc GetSpec(GenericContext context) => this;

    /// <summary> Creates an <see cref="ArrayType"/> of the current type. </summary>
    public virtual ArrayType CreateArray() => new(this);

    /// <summary> Creates a <see cref="PointerType"/> of the current type. </summary>
    public virtual PointerType CreatePointer() => new(this);

    /// <summary> Creates a <see cref="ByrefType"/> of the current type. </summary>
    public virtual ByrefType CreateByref() => new(this);

    /// <summary> Searches for a method with the specified signature. </summary>
    /// <param name="sig">The method signature to search for, or `default` to match any signature. Should not include the `this` parameter type. </param>
    /// <param name="spec">A generic context used to specialize methods before matching with the signature.</param>
    public virtual MethodDesc? FindMethod(string name, in MethodSig sig = default, in GenericContext spec = default, bool searchBaseAndItfs = false, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        foreach (var method in Methods) {
            if (method.Name == name && (sig.IsNull || sig.Matches(method, spec))) {
                return method;
            }
        }
        if (searchBaseAndItfs) {
            foreach (var itf in Interfaces) {
                if (itf.FindMethod(name, sig, spec, searchBaseAndItfs, throwIfNotFound: false) is { } method) {
                    return method;
                }
            }
            for (var parent = BaseType; parent != null; parent = BaseType) {
                if (parent.FindMethod(name, sig, spec, searchBaseAndItfs, throwIfNotFound: false) is { } method) {
                    return method;
                }
            }
        }
        if (throwIfNotFound) {
            throw new InvalidOperationException($"Could not find method '{name}' in type '{Name}'.");
        }
        return null;
    }

    public virtual FieldDesc? FindField(string name)
    {
        foreach (var field in Fields) {
            if (field.Name == name) {
                return field;
            }
        }
        return null;
    }

    /// <summary> Checks if this type is, inherits, or implements `baseType`. </summary>
    public bool Inherits(TypeDesc baseType)
    {
        for (var parent = this; parent != null; parent = parent.BaseType) {
            if (parent == baseType || (baseType.IsInterface && parent.Interfaces.Contains(baseType))) {
                return true;
            }
        }
        return false;
    }

    public sealed override void Print(PrintContext ctx) => Print(ctx, false);

    public virtual void Print(PrintContext ctx, bool includeNs = false)
    {
        var ns = Namespace;
        if (ns != null && includeNs) {
            ctx.Print(ns + ".");
        }
        ctx.Print(Name, IsValueType ? PrintToner.StructName : PrintToner.ClassName);
    }
    public override void PrintAsOperand(PrintContext ctx)
    {
        ctx.Print($"{PrintToner.Keyword}typeof{PrintToner.Default}(");
        Print(ctx, false);
        ctx.Print(")");
    }

    public abstract bool Equals(TypeDesc? other);

    public override bool Equals(object? obj) => obj is TypeDesc o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Kind, Name);

    public static bool operator ==(TypeDesc? a, TypeDesc? b) => object.ReferenceEquals(a, b) || (a is not null && a.Equals(b));
    public static bool operator !=(TypeDesc? a, TypeDesc? b) => !(a == b);
}