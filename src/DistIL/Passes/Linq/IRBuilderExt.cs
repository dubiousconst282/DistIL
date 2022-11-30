namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal static class IRBuilderExt
{
    //Note: this assumes that lambda types are all System.Func<>
    public static Value CreateLambdaInvoke(this IRBuilder ib, Value lambda, params Value[] args)
    {
        var invoker = lambda.ResultType.FindMethod("Invoke");
        return ib.CreateCallVirt(invoker, args.Prepend(lambda).ToArray());
    }
    public static Value CreateLambdaInvoke_ItemAndIndex(this IRBuilder ib, Value lambda, Value currItem, Value currIndex)
    {
        var invoker = lambda.ResultType.FindMethod("Invoke");

        var args = invoker.ParamSig.Count == 3
            ? new Value[] { lambda, currItem, currIndex }
            : new Value[] { lambda, currItem };
        return ib.CreateCallVirt(invoker, args);
    }
}