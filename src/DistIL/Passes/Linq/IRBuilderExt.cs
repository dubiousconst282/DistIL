namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal static class IRBuilderExt
{
    // Note: this assumes that lambda types are all System.Func<>
    public static Value CreateLambdaInvoke(this IRBuilder ib, Value lambda, params Value[] args)
    {
        return ib.CreateCallVirt("Invoke", args.Prepend(lambda).ToArray());
    }
    public static Value CreateLambdaInvoke_ItemAndIndex(this IRBuilder ib, Value lambda, Value currItem, LoopAccumVarFactory createAccum)
    {
        var invoker = lambda.ResultType.FindMethod("Invoke");

        if (invoker.ParamSig.Count == 3) {
            CallInst call = null!;
            createAccum(ConstInt.CreateI(0), currIndex => {
                call = ib.CreateCallVirt(invoker, lambda, currItem, currIndex);
                return ib.CreateAdd(currIndex, ConstInt.CreateI(1));
            });
            return call;
        }
        return ib.CreateCallVirt(invoker, lambda, currItem);
    }

    public static void Throw(this IRBuilder ib, Type exceptionType, Value? cond = null)
    {
        var modResolver = ib.Method.Definition.Module.Resolver;
        var exceptCtor = modResolver.Import(exceptionType)
            .FindMethod(".ctor", new MethodSig(PrimType.Void, [], isInstance: true));

        var throwHelper = ib.Method.CreateBlock().SetName("LQ_ThrowHelper");
        var exceptObj = new NewObjInst(exceptCtor, []);
        throwHelper.InsertLast(exceptObj);
        throwHelper.InsertLast(new ThrowInst(exceptObj));

        if (cond == null) {
            ib.SetBranch(throwHelper);
        } else {
            ib.ForkIf(ib.CreateNe(cond, Const.CreateZero(cond.ResultType)), throwHelper);
        }
    }
}