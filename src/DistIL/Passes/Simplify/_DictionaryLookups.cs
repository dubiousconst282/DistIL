namespace DistIL.Passes;

using DistIL.IR.Utils;

partial class SimplifyInsts
{
    // Optimize double dictionary lookups:
    //
    //  if (dict.ContainsKey(key))
    //    return dict[key];
    // ->
    //  if (dict.TryGetValue(key, out long val))
    //    return val;
    //
    //  BB_01:
    //    bool r2 = callvirt Dictionary`2[string, long]::ContainsKey(this: #dict, string: #key)
    //    goto r2 ? BB_04 : BB_07
    //  BB_04:
    //    long r5 = callvirt Dictionary`2[string, long]::get_Item(this: #dict, string: #key)
    //    ret r5
    // ->
    //  BB_01:
    //    long& r2 = varaddr $loc1
    //    bool r3 = callvirt Dictionary`2[string, long]::TryGetValue(this: #dict, string: #key, long&: r2)
    //    goto r3 ? BB_05 : BB_08
    //  BB_05: // preds: BB_01
    //    long r6 = ldvar $loc1
    //    ret r6
    private static bool SimplifyDictLookup(CallInst call)
    {
        Debug.Assert(call.Method.Name == "ContainsKey");

        if (!(call.Next is BranchInst br && br.Cond == call && call.Args is [TrackedValue instance, var key])) return false;

        var declType = (TypeSpec)call.Method.DeclaringType;
        // Only check for the get_Item() call once, because guaranteeing that the dictionary
        // won't change in between calls is tricky. (VN may handle this in the future.)
        if (br.Then.First is not CallInst { Method.Name: "get_Item" } getCall ||
            getCall.Method.DeclaringType != declType ||
            getCall.Args[0] != instance || getCall.Args[1] != key) return false;


        var builder = new IRBuilder(call, InsertionDir.After);
        
        var tempVar = builder.Method.CreateVar(declType.GenericParams[1], "dictLookupOut");
        call.ReplaceWith(builder.CreateCallVirt("TryGetValue", instance, key, tempVar));

        builder.SetPosition(getCall, InsertionDir.After);
        getCall.ReplaceWith(builder.CreateLoad(tempVar));

        return true;
    }
}