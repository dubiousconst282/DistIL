namespace DistIL.Passes;

using DistIL.IR.Utils;

/// <summary> Inlines fields of non-escaping object allocations into local variables. </summary>
public class ScalarReplacement : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var allocs = new List<NewObjInst>();
        
        // Find non-escaping object allocations
        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is NewObjInst alloc && !alloc.ResultType.IsValueType && IsSimpleCtor(alloc) && !Escapes(alloc)) {
                allocs.Add(alloc);
            }
        }

        // Replace fields with local variables
        foreach (var obj in allocs) {
            InlineAlloc(obj);
        }

        return allocs.Count > 0 ? MethodInvalidations.ControlFlow : 0; // ctors are inlined and may add new blocks.
    }

    private static void InlineAlloc(NewObjInst alloc)
    {
        // FieldSpecs don't have proper equality comparisons and may have multiple 
        // instances for the same spec, we must use the definition as key instead.
        var fieldSlots = new Dictionary<FieldDef, LocalSlot>();

        // Zero-init fields (this is necessary for correct SSA promotion)
        var builder = new IRBuilder(alloc, InsertionDir.Before);

        foreach (var field in alloc.ResultType.Fields) {
            builder.CreateInitObj(GetSlot(field));
        }

        // At this point we know that the constructor doesn't let the instance escape.
        // Inlining it here will add the accessed fields to the use chain of `alloc`.
        InlineMethods.Inline(alloc, (MethodDefOrSpec)alloc.Constructor, [alloc, ..alloc.Args]);

        foreach (var user in alloc.Users()) {
            if (user is FieldAddrInst addr) {
                addr.ReplaceWith(GetSlot(addr.Field));
            } else {
                Debug.Assert(IsObjectCtorCall(user));
                user.Remove();
            }
        }

        // Remove redundant allocation
        alloc.Remove();

        LocalSlot GetSlot(FieldDesc field)
        {
            var def = ((FieldDefOrSpec)field).Definition;
            return fieldSlots.GetOrAddRef(def) ??= builder.Method.CreateVar(field.Type, "sroa." + field.Name);
        }
    }

    private static bool IsSimpleCtor(NewObjInst alloc)
    {
        if (alloc.Constructor is not MethodDefOrSpec { Definition.Body: MethodBody body }) {
            return false;
        }
        // Ctor must be small and instance obj cannot escape
        return body.NumBlocks < 8 && !Escapes(body.Args[0], isCtor: true);
    }
    private static bool Escapes(TrackedValue obj, bool isCtor = false)
    {
        // Consider obj as escaping if it is being passed somewhere
        return !obj.Users().All(u => u is FieldAddrInst || (isCtor && IsObjectCtorCall(u)));
    }

    private static bool IsObjectCtorCall(Instruction inst)
    {
        return inst is CallInst { Method.Name: ".ctor", Method.DeclaringType: var declType } &&
            declType.IsCorelibType(typeof(object));
    }
}