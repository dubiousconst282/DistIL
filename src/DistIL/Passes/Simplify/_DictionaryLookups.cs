namespace DistIL.Passes;

partial class SimplifyInsts : MethodPass
{
    //Optimize double dictionary lookups:
    //
    //  if (dict.ContainsKey(key))
    //    return dict[key];
    //->
    //  if (dict.TryGetValue(key, out long val))
    //    return val;
    //
    //  BB_01:
    //    bool r2 = callvirt Dictionary`2[string, long]::ContainsKey(this: #dict, string: #key)
    //    goto r2 ? BB_04 : BB_07
    //  BB_04:
    //    long r5 = callvirt Dictionary`2[string, long]::get_Item(this: #dict, string: #key)
    //    ret r5
    //->
    //  BB_01:
    //    long& r2 = varaddr $loc1
    //    bool r3 = callvirt Dictionary`2[string, long]::TryGetValue(this: #dict, string: #key, long&: r2)
    //    goto r3 ? BB_05 : BB_08
    //  BB_05: //preds: BB_01
    //    long r6 = ldvar $loc1
    //    ret r6
    private bool SimplifyDictLookup(MethodTransformContext ctx, CallInst call)
    {
        Debug.Assert(call.Method.Name == "ContainsKey");

        var branch = (BranchInst?)call.Users().FirstOrDefault(u => u is BranchInst); //implies branch.Cond == call
        if (branch == null || call.Args is not [TrackedValue instance, var key]) return false;

        var declType = (TypeSpec)call.Method.DeclaringType;
        //Only check for the get_Item() call once, because guaranteeing that the dictionary
        //won't change in between calls is tricky. (VN may handle this in the future.)
        if (branch.Then.First is not CallInst { Method.Name: "get_Item" } getCall ||
            getCall.Method.DeclaringType != declType ||
            getCall.Args[0] != instance || getCall.Args[1] != key) return false;

        var resultVar = new Variable(declType.GenericParams[1]);
        var resultAddr = new VarAddrInst(resultVar);
        var resultLoad = new LoadVarInst(resultVar);
        var tryGetCall = new CallInst(
            declType.FindMethod("TryGetValue", 
                new MethodSig(PrimType.Bool, new TypeSig[] { key.ResultType, resultVar.ResultType.CreateByref() }))!,
            new[] { instance, key, resultAddr }, isVirtual: true);

        resultAddr.InsertBefore(call);
        call.ReplaceWith(tryGetCall, insertIfInst: true);
        resultLoad.InsertBefore(getCall);
        getCall.ReplaceWith(resultLoad);

        return true;
    }
}