namespace DistIL.Passes;

/// <summary> Inline object/structs into local variables. aka "Scalar Replacement of Aggregates" </summary>
public class ScalarReplacement : MethodPass
{
    public override void Run(MethodTransformContext ctx)
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
            ctx.Logger.Info($"Scalarizing allocation for {obj.Constructor.DeclaringType}");
            InlineAlloc(obj);
        }
    }

    private static void InlineAlloc(NewObjInst alloc)
    {
        //FieldSpecs don't have proper equality comparisons and may have multiple 
        //instances for the same spec, we must use the definition as key instead.
        var fieldSlots = new Dictionary<FieldDef, Variable>();

        //At this point we know that the constructor doesn't let the instance escape,
        //inlining it here will add the accessed fields to the use chain of `alloc`.
        var ctorArgs = new Value[alloc.NumArgs + 1];
        ctorArgs[0] = alloc;
        alloc.Args.CopyTo(ctorArgs.AsSpan(1));
        InlineMethods.Inline(alloc, (MethodDefOrSpec)alloc.Constructor, ctorArgs);

        foreach (var user in alloc.Users()) {
            switch (user) {
                case LoadFieldInst load: {
                    var slot = GetSlot(load.Field);
                    load.ReplaceWith(new LoadVarInst(slot), insertIfInst: true);
                    break;
                }
                case FieldAddrInst addr: {
                    var slot = GetSlot(addr.Field);
                    slot.IsExposed = true;
                    addr.ReplaceWith(new VarAddrInst(slot), insertIfInst: true);
                    break;
                }
                case StoreFieldInst store: {
                    var slot = GetSlot(store.Field);
                    store.ReplaceWith(new StoreVarInst(slot, store.Value), insertIfInst: true);
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
        //Allocation is redundant now
        alloc.Remove();

        Variable GetSlot(FieldDesc field)
        {
            var def = ((FieldDefOrSpec)field).Definition;
            return fieldSlots.GetOrAddRef(def) ??= new Variable(field.Sig, "sroa." + field.Name);
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
        return !obj.Users().All(u => 
            (u is LoadFieldInst or FieldAddrInst) || 
            (u is StoreFieldInst st && st.Value != obj) ||
            (isCtor && IsObjectCtorCall(u))
        );
    }

    private static bool IsObjectCtorCall(Instruction inst)
    {
        return inst is CallInst { Method.Name: ".ctor", Method.DeclaringType: var declType } &&
            IsSystemType(declType, typeof(object));
    }

    //TODO: make this a public extension method
    private static bool IsSystemType(TypeDesc desc, Type rtType)
    {
        return desc is TypeDefOrSpec def && 
               def.Module == def.Module.Resolver.CoreLib && 
               def.Name == rtType.Name &&
               def.Namespace == rtType.Namespace;
    }
}