namespace DistIL.Passes;

partial class SimplifyInsts : MethodPass
{
    //Lambda with cache:
    //Func = Func`2[int, bool]
    //  BB_Header:
    //    Func r2 = ldfld Data::LambdaCache1
    //    int r3 = cmp.ne r2, null
    //    goto r3 ? BB_Result : BB_CacheLoad
    //  BB_CacheLoad:
    //    Data r5 = ldfld Data::Instance
    //    delegate* r6 = funcaddr bool Data::Lambda1(Data, int)
    //    Func r7 = newobj Func::.ctor(object: r5, nint: r6)
    //    stfld Data::LambdaCache1, r7
    //    goto BB_Result
    //  BB_Result:
    //    Func r9 = phi [BB_Header -> r2], [BB_CacheLoad -> r7]
    //    ...
    //    bool r25 = callvirt Func::Invoke(this: r9, int: r24)
    private bool DirectizeLambda(CallInst call)
    {
        var method = call.Method;
        if (!(
            method.Name == "Invoke" &&
            t_Delegate != null && method.DeclaringType.Inherits(t_Delegate) &&
            call is { IsStatic: false, Args: [var lambdaInstance, ..] }
        )) return false;

        if (lambdaInstance is PhiInst { NumArgs: 2 } phi) {
            var phiArg1 = phi.GetValue(0);
            var phiArg2 = phi.GetValue(1);
            var allocInst = (phiArg1 as NewObjInst) ?? (phiArg2 as NewObjInst);
            var cacheLoad = (phiArg1 as LoadFieldInst) ?? (phiArg2 as LoadFieldInst);

            if (allocInst == null || cacheLoad == null || !DirectizeWithCtorArgs(call, allocInst)) return false;

            //Last lambda to be inlined is responsible for cleanup
            if (phi.NumUses == 0) {
                DeleteCache(phi, allocInst, cacheLoad);
            }
            return true;
        } else if (lambdaInstance is NewObjInst immAlloc) {
            if (!DirectizeWithCtorArgs(call, immAlloc)) return false;
            
            if (immAlloc.NumUses == 0) {
                immAlloc.Remove();
            }
            return true;
        }
        return false;

        static bool DirectizeWithCtorArgs(CallInst call, NewObjInst alloc)
        {
            if (alloc is not { Args: [LoadFieldInst ownerObj, FuncAddrInst funcAddr] }) return false;
            if (call.NumArgs != funcAddr.Method.Params.Length) return false;

            call.Method = funcAddr.Method;
            call.SetArg(0, ownerObj);
            return true;
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
                cacheStore.Field == cacheLoad.Field
            )) return false;

            br.Cond = ConstInt.CreateI(0); //We can't change the CFG, leave this for DCE.
            phi.Remove();
            cacheStore.Remove();
            allocInst.Remove();
            return true;
        }
    }
}