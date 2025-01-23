namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

//First{OrDefault}(pred?)
internal class FindSink : LinqSink
{
    public FindSink(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        var exit = loopData.Exit;
        string op = SubjectCall.Method.Name;

        // First(source, predicate)
        // FirstOrDefault(source, predicate?, defaultValue?)
        if (SubjectCall.Method.ParamSig is [_, { Type.Name: "Func`2" }, ..]) {
            // goto pred(currItem) ? NextBody : Latch
            var cond = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
            builder.ForkIf(cond, loopData.SkipBlock, negate: true);
        }
        
        if (op.EndsWith("OrDefault")) {
            // Body: goto Exit
            // Exit: var result = phi [Header: default(T)], [Body: currItem]
            var type = SubjectCall.ResultType;
            var defaultValue = SubjectCall.Method.ParamSig[^1] == type
                ? SubjectCall.Args[^1]
                : loopData.PreHeader.CreateDefaultOf(type);
            var phi = exit.CreatePhi(type, (builder.Block, currItem), (loopData.Header.Block, defaultValue));
            SubjectCall.ReplaceUses(phi);
        } else {
            // Body:  goto Exit2
            // Exit:  goto ThrowHelper
            // Exit2: var result = currItem
            exit.Throw(typeof(InvalidOperationException));
            exit.SetPosition(exit.Method.CreateBlock(insertAfter: exit.Block));
            SubjectCall.ReplaceUses(currItem);
        }
        // LoopBuilder expects an edge to the latch from the last body block, 
        // create a dead cond branch here so it won't complain about it.
        builder.ForkIf(ConstInt.CreateI(1), exit.Block);
    }
}

//Any(pred), All(pred)
internal class QuantifySink : LinqSink
{
    public QuantifySink(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
    {
        // Any():
        //  Body: goto !pred(currItem) ? Latch : Exit
        //  Exit: var result = phi [Body: true], [Header: false]
        // All():
        //  Body: goto pred(currItem) ? Latch : Exit
        //  Exit: var result = phi [Body: false], [Header: true]
        var exit = loopData.Exit;
        int normRes = SubjectCall.Method.Name == "Any" ? 0 : 1;

        var phi = exit.CreatePhi(PrimType.Bool,
            (builder.Block, ConstInt.CreateI(1 - normRes)),
            (loopData.Header.Block, ConstInt.CreateI(normRes)));
        SubjectCall.ReplaceUses(phi);

        var cond = builder.CreateLambdaInvoke(SubjectCall.Args[1], currItem);
        builder.ForkIf(builder.CreateNe(cond, ConstInt.CreateI(normRes)), exit.Block);
    }
}
