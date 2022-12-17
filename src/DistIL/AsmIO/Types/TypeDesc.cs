namespace DistIL.AsmIO;

/// <summary> The base class of all types. </summary>
public abstract class TypeDesc : EntityDesc, IEquatable<TypeDesc>
{
    public abstract TypeKind Kind { get; }
    public abstract StackType StackType { get; }

    public abstract string? Namespace { get; }

    public abstract TypeDesc? BaseType { get; }
    public virtual IReadOnlyList<TypeDesc> Interfaces => Array.Empty<TypeDesc>();
    public virtual IReadOnlyList<TypeDesc> GenericParams { get; } = Array.Empty<TypeDesc>();

    /// <summary> Element type of the array, pointer or byref type. </summary>
    public virtual TypeDesc? ElemType => null;
    
    public virtual bool IsValueType => false;
    public virtual bool IsEnum => false;
    public virtual bool IsInterface => false;
    public bool IsGeneric => GenericParams.Count > 0;

    [MemberNotNullWhen(true, nameof(IsEnum))]
    public virtual TypeDesc? UnderlyingEnumType => null;

    static readonly List<MethodDesc> s_EmptyMethodList = new();
    static readonly List<FieldDesc> s_EmptyFieldList = new();
    public virtual IReadOnlyList<MethodDesc> Methods { get; } = s_EmptyMethodList;
    public virtual IReadOnlyList<FieldDesc> Fields { get; } = s_EmptyFieldList;

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
    public virtual MethodDesc? FindMethod(
        string name, in MethodSig sig = default, in GenericContext spec = default,
        bool searchBaseAndItfs = false, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        foreach (var method in Methods) {
            if (method.Name == name && (sig.IsNull || sig.Matches(method, spec))) {
                return method;
            }
        }
        if (searchBaseAndItfs) {
            foreach (var itf in Interfaces) {
                if (itf.FindMethod(name, sig, spec, searchBaseAndItfs, throwIfNotFound: false) is { } itfMethod) {
                    return itfMethod;
                }
            }
            if (BaseType?.FindMethod(name, sig, spec, searchBaseAndItfs, throwIfNotFound: false) is { } baseMethod) {
                return baseMethod;
            }
        }
        if (throwIfNotFound) {
            throw new InvalidOperationException($"Could not find method '{name}' in type '{Name}'.");
        }
        return null;
    }

    //TODO: consistency with FindMethod(), add `searchBaseAndItfs` parameter
    public virtual FieldDesc? FindField(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        foreach (var field in Fields) {
            if (field.Name == name) {
                return field;
            }
        }
        if (throwIfNotFound) {
            throw new InvalidOperationException($"Could not find field '{name}' in type '{Name}'.");
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

    /// <summary> Checks whether this type can be assigned to a variable of the given type, based on stronger typing rules. </summary>
    /// <remarks> Note that support for ArrayType is not implemented. </remarks>
    public bool IsAssignableTo(TypeDesc assigneeType)
    {
        Debug.Assert(this is not ArrayType && assigneeType is not ArrayType); //not impl, see comment on GetCommonAncestor()

        if (this == assigneeType) {
            return true;
        }
        var t1 = StackType;
        var t2 = assigneeType.StackType;

        if (t1 != t2) {
            return AreImplicitlyCompatible(t1, t2);
        }
        if (t1 is StackType.Int or StackType.Float) {
            return Kind.BitSize() <= assigneeType.Kind.BitSize();
        }
        if (t1 == StackType.Object) {
            return Inherits(assigneeType);
        }
        return t1 != StackType.Struct;
    }

    /// <summary> Checks whether this type can be assigned to a variable of the given type, assuming they are values on the evaluation stack. </summary>
    /// <remarks>
    /// - If both types are objects, true is always returned. <br/>
    /// - If both types are structs, returns whether they're equal. <br/>
    /// - Implies that an integer value stored on another location might be truncated, e.g. `int32.IsStackAssignableTo(int16) = true`. <br/>
    /// </remarks>
    public bool IsStackAssignableTo(TypeDesc assigneeType)
    {
        var t1 = StackType;
        var t2 = assigneeType.StackType;
        if (t1 != t2) {
            return AreImplicitlyCompatible(t1, t2);
        }
        if (t1 is StackType.Struct) {
            return this == assigneeType;
        }
        return true;
    }
    private static bool AreImplicitlyCompatible(StackType t1, StackType t2)
    {
        //Allow implicit conversions: nint <-> byref, nint <-> int
        return (t1 is StackType.NInt && t2 is StackType.ByRef or StackType.Int) ||
               (t2 is StackType.NInt && t1 is StackType.ByRef or StackType.Int);
    }

    /// <summary>
    /// Returns a type for a location in which two values of type `a` and `b` can be interchangeably assigned to, or null if they're not compatible. <br/>
    /// - If they're int or float types, returns whichever is bigger, e.g. `GetCommonAssignableType(ushort, int) = int`. Result is normalized to signed if the two types are mixed. <br/>
    /// - If they're object types, returns the common ancestor type. <br/>
    /// - If they're pointer or byref types, returns a pointer of the common assignable element type, or `void*`/`void&amp;` if they're different element types. <br/>
    /// - If they're object types, returns the lowest common ancestor, ending in Object. <br/>
    /// - Otherwise, if they're not stack assignable to each other, returns null. <br/>
    /// <br/>
    /// This is loosely based on `I.8.7.3 General assignment compatibility`.
    /// </summary>
    public static TypeDesc? GetCommonAssignableType(TypeDesc? a, TypeDesc? b)
    {
        if (a == null || b == null) {
            return a ?? b;
        }
        if (a == b) {
            return a;
        }
        var stA = a.StackType;
        var stB = b.StackType;

        if (stA == stB && stA != StackType.Struct) {
            if (stA is StackType.Int or StackType.Float) {
                return b.Kind.BitSize() > a.Kind.BitSize() || (b.Kind.IsSigned() && !a.Kind.IsSigned()) ? b : a;
            }
            if (stA == StackType.Object) {
                return GetCommonAncestor(a, b);
            }
            if (a is PointerType || b is PointerType) {
                return GetCommonPtrElemType(a, b).CreatePointer();
            }
            return a;
        }
        if (stA == StackType.ByRef || stB == StackType.ByRef) {
            return GetCommonPtrElemType(a, b).CreateByref();
        }
        if ((stA == StackType.Int && stB == StackType.NInt) || (stA == StackType.NInt && stB == StackType.Int)) {
            return stA == StackType.NInt ? a : b;
        }
        return null;
        
        static TypeDesc GetCommonPtrElemType(TypeDesc a, TypeDesc b)
        {
            a = a.ElemType!;
            b = b.ElemType!;
            var res = GetCommonAssignableType(a, b);
            return a == null || b == null || res == null ? PrimType.Void : res;
        }
    }

    public static TypeDesc GetCommonAncestor(TypeDesc a, TypeDesc b)
    {
        Ensure.That(!a.IsValueType && !b.IsValueType); //Not impl, will return ValueType as lowest CA

        int depthA = GetDepth(a);
        int depthB = GetDepth(b);

        //Sort `a, b` such that `a` is lower on the hierarchy
        if (depthA > depthB) {
            (a, b) = (b, a);
            (depthA, depthB) = (depthB, depthA);
        }
        //Walk down hierarchy of `b` to the same height as `a`
        for (int i = depthB; i > depthA; i--) {
            b = b.BaseType!;
        }
        //Check for intersecting ancestors
        var tempSet = default(HashSet<TypeDesc>);

        for (int i = depthA; i > 0; i--) {
            if (a == b) {
                return a;
            }
            if (a.Interfaces.Count > 0 && b.Interfaces.Count > 0) {
                //Try return the first intersection between a.Interfaces and b.Interfaces
                //
                //Note that this won't work for e.g. PrimType.Array, in the worst case this
                //will just fallback to Object, which is fine as far as CIL is concerned.
                (tempSet ??= new()).Clear();

                foreach (var itf in a.Interfaces) {
                    tempSet.Add(itf);
                }
                foreach (var itf in b.Interfaces) {
                    if (tempSet.Contains(itf)) {
                        return itf;
                    }
                }
            }
            a = a.BaseType!;
            b = b.BaseType!;
        }
        return PrimType.Object;

        static int GetDepth(TypeDesc type)
        {
            int depth = 0;
            while ((type = type.BaseType!) != null) {
                depth++;
            }
            return depth;
        }
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