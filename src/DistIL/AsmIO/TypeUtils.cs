namespace DistIL.AsmIO;

using System.Reflection;

public static class TypeUtils
{
    /// <summary> Checks if this type is defined in <c>System.Private.CoreLib</c>. </summary>
    public static bool IsCorelibType(this TypeDesc desc)
    {
        return desc is TypeDefOrSpec def && def.Module == def.Module.Resolver.CoreLib;
    }
    
    /// <summary> Checks if this type is the definition of a type in CoreLib, <paramref name="rtType"/>. </summary>
    /// <remarks> This method compares by names, generic instantiations are not considered. </remarks>
    public static bool IsCorelibType(this TypeDesc desc, Type rtType)
    {
        return desc.IsCorelibType() &&
               desc.Name == rtType.Name &&
               desc.Namespace == rtType.Namespace;
    }

    /// <summary> Whether this type represents a primitive integer (byte, int, long, etc.). </summary>
    public static bool IsInt(this TypeDesc type)
    {
        return type.StackType is StackType.Int or StackType.Long;
    }
    /// <summary> Whether this type represents a float or double. </summary>
    public static bool IsFloat(this TypeDesc type)
    {
        return type.StackType is StackType.Float;
    }

    /// <summary> Whether this type represents a byref, pointer, or nint. </summary>
    public static bool IsPointerLike(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt;
    }

    public static bool IsPointerOrObject(this TypeDesc type)
    {
        return type.StackType is StackType.ByRef or StackType.NInt or StackType.Object;
    }

    public static bool IsManagedObject(this TypeDesc type)
    {
        return type.StackType is StackType.Object;
    }

    /// <summary> If this type is generic, returns an <see cref="TypeSpec"/> with all unbound parameters. Otherwise, returns the unchanged instance. </summary>
    public static TypeDesc GetUnboundSpec(this TypeDesc type)
    {
        return type is TypeDefOrSpec { Definition: var def } 
            ? def.GetSpec(GenericContext.Empty)
            : type;
    }

    /// <summary> Checks if the given value has a concrete result object type (it's statically known). </summary>
    public static bool HasConcreteType(Value obj)
    {
        // TODO: could probably build a more sophisticated analysis for this, with propagation / use chain scan
        return obj.ResultType is TypeDefOrSpec def && def.Attribs.HasFlag(TypeAttributes.Sealed) ||
               obj is NewObjInst;
    }

    public static MethodDesc? ResolveVirtualMethod(MethodDesc method, Value instanceObj)
    {
        return HasConcreteType(instanceObj) ? ResolveVirtualMethod(method, instanceObj.ResultType) : null;
    }

    /// <summary> Resolves the implementation of the given virtual method defined by a concrete instance type. </summary>
    public static MethodDesc? ResolveVirtualMethod(MethodDesc method, TypeDesc actualType)
    {
        Ensure.That(method.Attribs.HasFlag(MethodAttributes.Virtual));
        Ensure.That(actualType.Inherits(method.DeclaringType));

        // FIXME: spec conformance
        // - II.10.3.4 Impact of overrides on derived classes
        // - II.12.2 Implementing virtual methods on interfaces
        // - https://github.com/dotnet/runtime/blob/main/docs/design/specs/Ecma-335-Augments.md#ii122-implementing-virtual-methods-on-interfaces

        var type = (TypeDefOrSpec)actualType;
        var sig = default(MethodSig?);

        var genCtx = new GenericContext(actualType.GenericParams, method.GenericParams);

        for (; type != null; type = type.BaseType) {
            // Try get from MethodImpl table
            var methodDef = ((MethodDefOrSpec)method).Definition;
            if (type.Definition.MethodImpls.TryGetValue(methodDef, out var explicitImpl)) {
                return explicitImpl.GetSpec(genCtx);
            }

            // Search for method with matching sig
            sig ??= new MethodSig(methodDef.ReturnSig, methodDef.ParamSig.Skip(1).ToList(), isInstance: true, method.GenericParams.Count);

            if (type.FindMethod(method.Name, sig.Value, throwIfNotFound: false) is { } matchImpl) {
                Debug.Assert(!matchImpl.Attribs.HasFlag(MethodAttributes.Abstract));
                return matchImpl.GetSpec(genCtx);
            }
        }
        return null;
    }


    /// <summary> Checks if the given object can be cast to <paramref name="destType"/>, or null if unknown. </summary>
    public static bool? CheckCast(Value obj, TypeDesc destType)
    {
        var srcType = obj.ResultType;
        bool castable = srcType.IsAssignableTo(destType);

        // If the object type is known or already assignable to `destType`, we don't need to look further.
        if (castable || HasConcreteType(obj)) {
            return castable;
        }

        // Impossible cast: Both `srcType` and `destType` are classes, but `destType` doesn't inherit from srcType
        if (srcType.IsClass && destType.IsClass && !destType.Inherits(srcType)) {
            return false;
        }
        // Impossible cast: `destType` is an array but `srcType` is some other def
        if (destType is ArrayType && !srcType.IsInterface && srcType != PrimType.Object && srcType != PrimType.Array) {
            return false;
        }

        // No idea
        return null;
    }

    /// <summary> Checks if this type is or contains managed references. May return null if the type is an open generic. </summary>
    /// <remarks> See <see cref="System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences{T}"/> </remarks>
    public static bool? IsRefOrContainsRefs(this TypeDesc type)
    {
        if (type is TypeDefOrSpec { IsValueType: true }) {
            return type.Fields.All(f => !f.IsInstance || IsRefOrContainsRefs(f.Type) is false);
        }
        if (type.StackType is not (StackType.Object or StackType.ByRef)) {
            return false;
        }
        return null; // possibly a generic type parameter
    }
}