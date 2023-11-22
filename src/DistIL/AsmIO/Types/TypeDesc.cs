namespace DistIL.AsmIO;

/// <summary> Describes a type declaration. </summary>
public abstract class TypeDesc : EntityDesc, IEquatable<TypeDesc>
{
    public abstract TypeKind Kind { get; }
    public abstract StackType StackType { get; }

    public abstract string Name { get; }
    public abstract string? Namespace { get; }

    public abstract TypeDesc? BaseType { get; }
    public virtual IReadOnlyList<TypeDesc> Interfaces => [];
    public virtual IReadOnlyList<TypeDesc> GenericParams { get; } = [];

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

    // Cached compound types
    private ArrayType? _arrayType;
    private PointerType? _ptrType;
    private ByrefType? _byrefType;

    /// <summary>
    /// Creates a generic type instantiation with the given context as arguments, 
    /// or returns the current instance if it is not a generic type definition.
    /// </summary>
    public virtual TypeDesc GetSpec(GenericContext context) => this;

    /// <summary> Creates an <see cref="ArrayType"/> of the current type. </summary>
    public ArrayType CreateArray() => _arrayType ??= new(this);

    /// <summary> Creates a <see cref="PointerType"/> of the current type. </summary>
    public PointerType CreatePointer() => _ptrType ??= new(this);

    /// <summary> Creates a <see cref="ByrefType"/> of the current type. </summary>
    public ByrefType CreateByref() => _byrefType ??= new(this);

    /// <summary> Searches for a method with the specified signature. </summary>
    /// <param name="sig">
    /// The method signature to search for, or <see langword="default"/> to match any signature. <br/>
    /// If the target method is generic, this should contain unbound parameters rather than arguments.
    /// It also should not include the instance parameter type.
    /// </param>
    public virtual MethodDesc? FindMethod(
        string name, in MethodSig sig = default,
        bool searchBaseAndItfs = false, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        foreach (var method in Methods) {
            if (method.Name == name && (sig.IsNull || sig.Matches(method))) {
                return method;
            }
        }
        if (searchBaseAndItfs) {
            foreach (var itf in Interfaces) {
                if (itf.FindMethod(name, sig, searchBaseAndItfs, throwIfNotFound: false) is { } itfMethod) {
                    return itfMethod;
                }
            }
            if (BaseType?.FindMethod(name, sig, searchBaseAndItfs, throwIfNotFound: false) is { } baseMethod) {
                return baseMethod;
            }
        }
        if (throwIfNotFound) {
            throw new InvalidOperationException($"Could not find method '{name}' in type '{Name}'.");
        }
        return null;
    }

    // TODO: consistency with FindMethod(), add `searchBaseAndItfs` parameter
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

    /// <summary> Checks if this type is, inherits, or implements <paramref name="baseType"/>. </summary>
    public bool Inherits(TypeDesc baseType)
    {
        for (var parent = this; parent != null; parent = parent.BaseType) {
            if (parent == baseType || (baseType.IsInterface && Implements(parent, (TypeDefOrSpec)baseType))) {
                return true;
            }
        }
        return false;
    }

    private static bool Implements(TypeDesc type, TypeDefOrSpec itf, bool isForAssignmentCheck = false)
    {
        if (type is PrimType prim) {
            var resolver = itf.Module.Resolver;
            type = prim.GetDefinition(resolver);
        }
        // Arrays actually implement lots of interfaces at runtime
        // I.8.7.1 only mentions IList, but there are many others.
        else if (type is ArrayType arr) {
            return arr.Implements(itf);
        }

        if (itf.IsGeneric && itf.Definition.GenericParams.Any(g => g.IsCovariant)) {
            return type.Interfaces.Any(m => CheckCovariant(m, itf)) || 
                   (isForAssignmentCheck && CheckCovariant(type, itf));
        }
        return type.Interfaces.Contains(itf) || (isForAssignmentCheck && type == itf);

        static bool CheckCovariant(TypeDesc impl, TypeDefOrSpec itf)
        {
            var itfDef = itf.Definition;
            // Check for the generic def, TypeDefs instances are unique.
            if (ReferenceEquals(impl, itfDef)) {
                return true;
            }
            if (impl is not TypeSpec implSpec || implSpec.Definition != itfDef) {
                return false;
            }
            int count = itf.GenericParams.Count;

            for (int i = 0; i < count; i++) {
                var typeA = implSpec.GenericParams[i];
                var typeB = itf.GenericParams[i];

                if (typeA.IsValueType || typeB.IsValueType ? typeA != typeB : !typeA.Inherits(typeB)) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary> Checks whether this type can be assigned to a variable of the given type, based on stronger typing rules. </summary>
    /// <remarks> Note that support for ArrayType is not implemented. </remarks>
    public bool IsAssignableTo(TypeDesc assigneeType)
    {
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
            return IsInterface
                ? assigneeType == PrimType.Object || 
                    (assigneeType.IsInterface && Implements(this, (TypeDefOrSpec)assigneeType, isForAssignmentCheck: true))
                : Inherits(assigneeType);
        }
        return t1 != StackType.Struct; // structs of different types can't be assigned to each other
    }

    /// <summary> Checks whether this type can be assigned to a variable of the given type, assuming they are values on the evaluation stack. </summary>
    /// <remarks>
    /// - If both types are objects, true is always returned. <br/>
    /// - If both types are structs, returns whether they're equal. <br/>
    /// - Implies that an integer value stored on another location might be truncated, e.g. <c>int32.IsStackAssignableTo(int16) = true</c>. <br/>
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
        // Allow implicit conversions: nint <-> byref, nint <-> int
        return (t1 is StackType.NInt && t2 is StackType.ByRef or StackType.Int) ||
               (t2 is StackType.NInt && t1 is StackType.ByRef or StackType.Int);
    }

    /// <summary> Returns the common base type of <paramref name="a"/> and <paramref name="b"/>, assuming they're both object types (not structs). </summary>
    public static TypeDesc GetCommonAncestor(TypeDesc a, TypeDesc b)
    {
        Ensure.That(!a.IsValueType && !b.IsValueType); // Not impl, will return ValueType as lowest CA

        int depthA = GetDepth(a);
        int depthB = GetDepth(b);

        // Sort `a, b` such that `a` is lower on the hierarchy
        if (depthA > depthB) {
            (a, b) = (b, a);
            (depthA, depthB) = (depthB, depthA);
        }
        // Walk down hierarchy of `b` to the same height as `a`
        for (int i = depthB; i > depthA; i--) {
            b = b.BaseType!;
        }
        // Check for intersecting ancestors
        for (int i = depthA; i >= 0; i--) {
            if (a == b) {
                return a;
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