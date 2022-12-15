namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

public class LoopBuilder
{
    public readonly IRBuilder PreHeader, Header, Latch, Exit;
    public readonly IRBuilder Body;
    private List<(PhiInst HeadPhi, PhiInst LatchPhi, Instruction Next)> _pendingAccums = new();

    //PreHeader:
    //  ...
    //  goto Header
    //Header:
    //  var accumN = phi [PreHeader -> accumN_Seed, Latch -> accumN_Next]
    //  bool hasNext = createBound()
    //  goto hasNext ? Body : Exit
    //Body:
    //  ...
    //  goto Latch
    //Latch:
    //  var accumN_Next = update(accumN)
    //  goto Header
    //Exit:
    //  ...
    public LoopBuilder(Func<string, IRBuilder> createBlock)
    {
        PreHeader = createBlock("PreHeader");
        Header = createBlock("Header");
        Body = createBlock("Body");
        Latch = createBlock("Latch");
        Exit = createBlock("Exit");
    }

    public void Build(
        Func<IRBuilder, Value> emitCond,
        Action<IRBuilder> emitBody)
    {
        PreHeader.SetBranch(Header.Block);

        var hasNext = emitCond(Header);
        Header.SetBranch(hasNext, Body.Block, Exit.Block);

        emitBody(Body);
        Body.SetBranch(Latch.Block);

        foreach (var (headPhi, latchPhi, next) in _pendingAccums) {
            //Phi inputs must dominate their corresponding predecessor block,
            //we only support updates in the latch, or in a direct pred of it.
            Debug.Assert(next.Block.Succs.Contains(Latch.Block));

            foreach (var pred in Latch.Block.Preds) {
                latchPhi.AddArg(pred, pred == next.Block ? next : headPhi);
            }
        }
        Latch.SetBranch(Header.Block);
    }

    /// <summary> Creates a loop accumulator/induction variable. </summary>
    public Value CreateAccum(Value seed, Func<Value, Value> emitUpdate)
    {
        var phi = Header.CreatePhi(seed.ResultType);
        var next = emitUpdate(phi);

        if (next is Instruction inst && inst.Block != Latch.Block) {
            //We must build pending accum phis at the end, because the block in which
            //the update inst is might be incomplete when CreateAccum() is called.
            var latchPhi = Latch.CreatePhi(seed.ResultType);
            _pendingAccums.Add((phi, latchPhi, inst));
            next = latchPhi;
        }
        phi.AddArg((PreHeader.Block, seed), (Latch.Block, next));
        return phi;
    }

    /// <summary> Creates an `int32` index var which increments on every iteration. </summary>
    public Value CreateInductor()
    {
        return CreateAccum(
            seed: ConstInt.CreateI(0), 
            emitUpdate: curr => Latch.CreateAdd(curr, ConstInt.CreateI(1))
        );
    }

    public void InsertBefore(Instruction inst)
    {
        var newBlock = inst.Block.Split(inst, branchTo: PreHeader.Block);
        Exit.SetBranch(newBlock);
    }
}