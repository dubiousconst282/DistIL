namespace DistIL.Passes.Linq;

using DistIL.IR;
using DistIL.IR.Utils;

//Handles First[OrDefault]
//
//First:
//  Body: goto Exit2
//  Exit: goto ThrowHelper
//  Exit2:
//    var result = currItem
//
//FirstOrDefault:
//  Body: goto Exit
//  Exit:
//    var result = phi [Header -> default()], [Body -> currItem]
//
internal class PeekFirstQuery : LinqQuery
{
    public PeekFirstQuery(CallInst call)
        : base(call) { }

    public override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
    {
        var exit = loopData.Exit;
        var op = SubjectCall.Method.Name;

        if (op.EndsWith("OrDefault")) {
            var type = SubjectCall.ResultType;
            var defaultValue = loopData.PreHeader.CreateDefaultOf(type);
            var phi = exit.CreatePhi(type, (builder.Block, currItem), (loopData.Header.Block, defaultValue));
            SubjectCall.ReplaceUses(phi);
        } else {
            exit.Fork((builder, newBlock) => builder.Throw(typeof(InvalidOperationException)));
            SubjectCall.ReplaceUses(currItem);
        }
        //LoopBuilder expects an edge to the latch from the last body block, 
        //create a dead cond branch here so it won't complain about it.
        builder.Fork(ConstInt.CreateI(0), exit.Block);
    }
}