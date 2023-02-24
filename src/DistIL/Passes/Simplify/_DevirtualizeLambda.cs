namespace DistIL.Passes;

using System.Runtime.CompilerServices;

partial class SimplifyInsts
{
    //Directize delegate invokes if target is known:
    //
    //Func = Func`2[int, bool]
    //  BB_Header:
    //    r2 = ldfld Data::LambdaCache1 -> Func
    //    r3 = cmp.ne r2, null -> bool
    //    goto r3 ? BB_Result : BB_CacheLoad
    //  BB_CacheLoad:
    //    r5 = ldfld Data::Instance -> Data
    //    r6 = funcaddr bool Data::Lambda1(Data, int) -> void*
    //    r7 = newobj Func::.ctor(object: r5, nint: r6) -> Func
    //    stfld Data::LambdaCache1, r7
    //    goto BB_Result
    //  BB_Result:
    //    r9 = phi [BB_Header: r2], [BB_CacheLoad: r7] -> Func
    //    ...
    //    r25 = callvirt Func::Invoke(this: r9, int: r24) -> bool
    //->
    //  BB_Result:
    //    r5 = ldfld Data::Instance -> Data
    //    r25 = call Data::Lambda1(Data: r5, int: r24) -> bool
    private static bool DevirtualizeLambda(CallInst call)
    {
        if (call is not { Method.Name: "Invoke", IsStatic: false, Args: [var lambdaInstance, ..] }) return false;

        if (lambdaInstance is PhiInst { NumArgs: 2 } phi) {
            var phiArg1 = phi.GetValue(0);
            var phiArg2 = phi.GetValue(1);
            var allocInst = (phiArg1 as NewObjInst) ?? (phiArg2 as NewObjInst);
            var cacheLoad = (phiArg1 as LoadFieldInst) ?? (phiArg2 as LoadFieldInst);

            if (allocInst == null || cacheLoad == null || !DevirtWithCtorArgs(call, allocInst)) return false;

            //Last lambda to be inlined is responsible for cleanup
            if (phi.NumUses == 0) {
                DeleteCache(phi, allocInst, cacheLoad);
            }
            return true;
        } else if (lambdaInstance is NewObjInst immAlloc) {
            if (!DevirtWithCtorArgs(call, immAlloc)) return false;
            
            if (immAlloc.NumUses == 0) {
                immAlloc.Remove();
            }
            return true;
        }
        return false;

        static bool DevirtWithCtorArgs(CallInst call, NewObjInst alloc)
        {
            if (alloc is not { Args: [var instanceObj, FuncAddrInst { Method: var method }] }) return false;
            if (call.NumArgs - (method.IsStatic ? 1 : 0) != method.ParamSig.Count) return false;

            if (instanceObj is ConstNull) {
                call.ReplaceWith(new CallInst(method, call.Args[1..].ToArray()), insertIfInst: true);
                return true;
            } else if (method.IsInstance) {
                call.Method = method;
                //Create a new load before call to assert dominance
                if (instanceObj is LoadFieldInst { IsStatic: true, Field: var field }) {
                    var newLoad = new LoadFieldInst(field);
                    newLoad.InsertBefore(call);
                    instanceObj = newLoad;
                }
                call.SetArg(0, instanceObj);
                return true;
            }
            return false;
        }
        static bool DeleteCache(PhiInst phi, NewObjInst allocInst, LoadFieldInst cacheLoad)
        {
            var condBlock = cacheLoad.Block;

            if (!(
                //First load block's must end with "goto cache == null ? BB_CacheLoad : BB_Result"
                condBlock.Last is
                    BranchInst { Cond: CompareInst { Op: CompareOp.Ne, Left: var condCache, Right: ConstNull } } br &&
                    br.Then == phi.Block &&
                    br.Else == allocInst.Block &&
                    condCache == cacheLoad &&
                //BB_CacheLoad must store to the cache field
                allocInst.NumUses == 2 && //phi and next store
                allocInst.Next is StoreFieldInst cacheStore &&
                cacheStore.Field == cacheLoad.Field &&
                cacheStore.Field.DeclaringType is TypeDefOrSpec declType && 
                declType.Definition.GetCustomAttribs().Has(typeof(CompilerGeneratedAttribute))
            )) return false;

            br.Cond = ConstInt.CreateI(0); //We can't change the CFG, leave this for DCE.
            phi.Remove();
            cacheLoad.Remove();
            cacheStore.Remove();
            allocInst.Remove();
            return true;
        }
    }
}