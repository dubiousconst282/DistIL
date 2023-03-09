namespace DistIL.Passes;

using DistIL.IR.Utils;

/// <summary> Inline object/structs into local variables. aka "Scalar Replacement of Aggregates" </summary>
public class ScalarReplacement : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var allocs = new List<NewObjInst>();
        
        //Find non-escaping object allocations
        foreach (var inst in ctx.Method.Instructions()) {
            if (inst is NewObjInst alloc && IsSimpleCtor(alloc) && !Escapes(alloc)) {
                allocs.Add(alloc);
            }
        }

        //Replace fields with local variables
        foreach (var obj in allocs) {
            InlineAlloc(obj);
        }

        return allocs.Count > 0 ? MethodInvalidations.ControlFlow : 0; //ctors are inlined and may add new blocks.
    }

    private static void InlineAlloc(NewObjInst alloc)
    {
        //FieldSpecs don't have proper equality comparisons and may have multiple 
        //instances for the same spec, we must use the definition as key instead.
        var fieldSlots = new Dictionary<FieldDef, Variable>();

        //Zero-init fields
        var builder = new IRBuilder(alloc, InsertionDir.Before);

        foreach (var field in alloc.ResultType.Fields) {
            builder.CreateVarStore(GetSlot(field), builder.CreateDefaultOf(field.Type));
        }

        //At this point we know that the constructor doesn't let the instance escape,
        //inlining it here will add the accessed fields to the use chain of `alloc`.
        var ctorArgs = new Value[alloc.NumArgs + 1];
        ctorArgs[0] = alloc;
        alloc.Args.CopyTo(ctorArgs.AsSpan(1));
        InlineMethods.Inline(alloc, (MethodDefOrSpec)alloc.Constructor, ctorArgs);

        foreach (var user in alloc.Users()) {
            switch (user) {
                case FieldAddrInst addr: {
                    var slot = GetSlot(addr.Field);
                    ScalarizeRef(addr, slot);
                    break;
                }
                default: {
                    if (IsObjectCtorCall(user)) {
                        user.Remove(); //nop from the inlined ctor
                        break;
                    }
                    throw new UnreachableException();
                }
            }
        }

        //Remove redundant allocation
        alloc.Remove();

        Variable GetSlot(FieldDesc field)
        {
            var def = ((FieldDefOrSpec)field).Definition;
            return fieldSlots.GetOrAddRef(def) ??= new Variable(field.Sig, "sroa." + field.Name);
        }
    }

    private static void ScalarizeRef(FieldAddrInst addr, Variable slot)
    {
        foreach (var use in addr.Uses()) {
            switch (use.Parent) {
                case LoadInst load: {
                    load.ReplaceWith(new LoadVarInst(slot), insertIfInst: true);
                    break;
                }
                case StoreInst store: {
                    store.ReplaceWith(new StoreVarInst(slot, store.Value), insertIfInst: true);
                    break;
                }
                default: {
                    var varAddr = new VarAddrInst(slot);
                    varAddr.InsertBefore(use.Parent);
                    use.Operand = varAddr;
                    slot.IsExposed = true;
                    break;
                }
            }
        }
    }

    private static bool IsSimpleCtor(NewObjInst alloc)
    {
        if (alloc.Constructor is not MethodDefOrSpec { Definition.Body: MethodBody body }) {
            return false;
        }
        //Ctor must be small and instance obj cannot escape
        return body.NumBlocks < 8 && !Escapes(body.Args[0], isCtor: true);
    }
    private static bool Escapes(TrackedValue obj, bool isCtor = false)
    {
        //Consider obj as escaping if it is being passed somewhere
        return !obj.Users().All(u => u is FieldAddrInst || (isCtor && IsObjectCtorCall(u)));
    }

    private static bool IsObjectCtorCall(Instruction inst)
    {
        return inst is CallInst { Method.Name: ".ctor", Method.DeclaringType: var declType } &&
            declType.IsCorelibType(typeof(object));
    }
}