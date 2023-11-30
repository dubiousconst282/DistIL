namespace DistIL.Passes;

using System.Reflection;

internal class DevirtUtils
{
    public static MethodDesc? ResolveVirtualCallTarget(MethodDesc method, Value instanceObj)
    {
        return HasConcreteType(instanceObj) ? ResolveVirtualMethod(method, instanceObj.ResultType) : null;
    }

    /// <summary> Checks if the given value has a concrete result object type (it's statically known). </summary>
    public static bool HasConcreteType(Value obj)
    {
        // TODO: could probably build a more sophisticated analysis for this, with propagation / use chain scan
        return obj.ResultType is TypeDefOrSpec def && def.Attribs.HasFlag(TypeAttributes.Sealed) ||
               obj is NewObjInst;
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
        // var sig = default(MethodSig?);

        var genCtx = new GenericContext(actualType.GenericParams, method.GenericParams);

        for (; type != null; type = type.BaseType) {
            // Try get from MethodImpl table
            if (type.Definition.MethodImpls.TryGetValue(method, out var explicitImpl)) {
                return explicitImpl.GetSpec(genCtx);
            }

            // Search for method with matching sig
            // sig ??= new MethodSig(method.ReturnSig, method.ParamSig.Skip(1).ToList(), isInstance: true, method.GenericParams.Count);

            // if (type.FindMethod(method.Name, sig.Value, throwIfNotFound: false) is { } matchImpl) {
            //     Debug.Assert(!matchImpl.Attribs.HasFlag(MethodAttributes.Abstract));
            //     return matchImpl.GetSpec(genCtx);
            // }
        }
        return null;
    }
}