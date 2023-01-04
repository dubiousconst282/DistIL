namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

public class LoopBuilder
{
    public readonly IRBuilder PreHeader, Header, Latch, Exit;
    public readonly IRBuilder Body;
    private List<(PhiInst HeadPhi, Instruction Next)> _pendingAccums = new();

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
    public LoopBuilder(BasicBlock blockPos)
    {
        PreHeader = CreateBlock("PreHeader");
        Header = CreateBlock("Header");
        Body = CreateBlock("Body");
        Latch = CreateBlock("Latch");
        Exit = CreateBlock("Exit");

        IRBuilder CreateBlock(string name)
        {
            blockPos = blockPos.Method.CreateBlock(insertAfter: blockPos).SetName("LQ_" + name);
            return new IRBuilder(blockPos);
        }
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

        foreach (var (headPhi, next) in _pendingAccums) {
            headPhi.ReplaceOperand(next, InsertAccumPhis(Latch.Block, next, headPhi));
        }
        Latch.SetBranch(Header.Block);
    }

    //Naive algorithm that inserts phis for each predecessor to form strict SSA (def dominates all uses).
    private Value InsertAccumPhis(BasicBlock block, Instruction def, Instruction dom)
    {
        if (block == def.Block) {
            return def;
        }
        if (block == dom.Block) {
            return dom;
        }
        if (block.NumPreds == 1) {
            return InsertAccumPhis(block.Preds.First(), def, dom);
        }
        Debug.Assert(block.NumPreds > 0);

        var phi = block.InsertPhi(def.ResultType);
        foreach (var pred in block.Preds) {
            phi.AddArg(pred, InsertAccumPhis(pred, def, dom));
        }
        return phi;
    }

    /// <summary> Creates a loop accumulator/induction variable. </summary>
    public Value CreateAccum(Value seed, Func<Value, Value> emitUpdate)
    {
        var phi = Header.CreatePhi(seed.ResultType);
        var next = emitUpdate(phi);

        if (next is Instruction nextI && nextI.Block != Latch.Block) {
            _pendingAccums.Add((phi, nextI));
        }
        phi.AddArg((PreHeader.Block, seed), (Latch.Block, next));
        return phi;
    }

    /// <summary> Creates an <see cref="int"/> index var which increments on every iteration. </summary>
    public Value CreateInductor()
    {
        return CreateAccum(
            seed: ConstInt.CreateI(0),
            emitUpdate: curr => Latch.CreateAdd(curr, ConstInt.CreateI(1))
        ).SetName("lq_index");
    }

    public void InsertBefore(Instruction inst)
    {
        var newBlock = inst.Block.Split(inst, branchTo: PreHeader.Block);
        Exit.SetBranch(newBlock);
    }
}