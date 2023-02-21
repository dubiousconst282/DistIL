namespace DistIL.Passes;

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
        var fieldSlots = new Dictionary<FieldDef, (Variable Var, int NumStores)>();

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
                    var slot = GetSlot(store.Field, incStores: true);
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
        //Remove redundant allocation
        alloc.Remove();

        //Promote slots with a single store
        foreach (var (slot, numStores) in fieldSlots.Values) {
            if (numStores == 1 && !slot.IsExposed) {
                var store = slot.Users().OfType<StoreVarInst>().First();
                store.Remove();

                foreach (var acc in slot.Users()) {
                    Debug.Assert(acc is LoadVarInst);
                    acc.ReplaceWith(store.Value);
                }
            }
        }

        Variable GetSlot(FieldDesc field, bool incStores = false)
        {
            var def = ((FieldDefOrSpec)field).Definition;
            ref var slot = ref fieldSlots.GetOrAddRef(def);

            if (incStores) {
                slot.NumStores++;
            }
            return slot.Var ??= new Variable(field.Sig, "sroa." + field.Name);
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
            declType.IsCorelibType(typeof(object));
    }
}