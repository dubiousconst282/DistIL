namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

//First{OrDefault}(pred?)
internal class FindFirstQuery : LinqQuery
{
    public FindFirstQuery(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var exit = loopData.Exit;
        string op = SubjectCall.Method.Name;

        //First(source, predicate)
        //FirstOrDefault(source, predicate?, defaultValue?)
        if (SubjectCall.Method.ParamSig is [_, { Type.Name: "Func`2" }, ..]) {
            //goto pred(currItem) ? NextBody : Latch
            var cond = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
            builder.Fork(cond, loopData.SkipBlock);
        }
        
        if (op.EndsWith("OrDefault")) {
            //Body: goto Exit
            //Exit: var result = phi [Header: default(T)], [Body: currItem]
            var type = SubjectCall.ResultType;
            var defaultValue = SubjectCall.Method.ParamSig[^1] == type
                ? SubjectCall.Args[^1]
                : loopData.PreHeader.CreateDefaultOf(type);
            var phi = exit.CreatePhi(type, (builder.Block, currItem), (loopData.Header.Block, defaultValue));
            SubjectCall.ReplaceUses(phi);
        } else {
            //Body:  goto Exit2
            //Exit:  goto ThrowHelper
            //Exit2: var result = currItem
            exit.Fork((builder, newBlock) => builder.Throw(typeof(InvalidOperationException)));
            SubjectCall.ReplaceUses(currItem);
        }
        //LoopBuilder expects an edge to the latch from the last body block, 
        //create a dead cond branch here so it won't complain about it.
        builder.Fork(ConstInt.CreateI(0), exit.Block);
    }
}
