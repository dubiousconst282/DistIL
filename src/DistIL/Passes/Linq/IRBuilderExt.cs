namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal static class IRBuilderExt
{
    //Note: this assumes that lambda types are all System.Func<>
    public static Value CreateLambdaInvoke(this IRBuilder ib, Value lambda, params Value[] args)
    {
        var invoker = lambda.ResultType.Methods.First(m => m.Name == "Invoke");
        return ib.CreateCallVirt(invoker, args);
    }
    public static Value CreateLambdaInvoke_ItemAndIndex(this IRBuilder ib, Value lambda, Value currItem, Value currIndex)
    {
        var type = lambda.ResultType;
        var invoker = type.Methods.First(m => m.Name == "Invoke");

        var args = invoker.ParamSig.Count == 3
            ? new Value[] { lambda, currItem, currIndex }
            : new Value[] { lambda, currItem };
        return ib.CreateCallVirt(invoker, args);
    }

    public static void ForkAndSkipIfFalse(this IRBuilder ib, Value cond, BasicBlock skipBlock)
    {
        var newBlock = ib.Block.Method.CreateBlock(insertAfter: ib.Block);
        ib.SetBranch(cond, newBlock, skipBlock);
        ib.SetPosition(newBlock);
    }
}