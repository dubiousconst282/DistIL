namespace DistIL.IR.Utils;

/// <summary> Helper for building a for-style loop. </summary>
public class LoopBuilder
{
    public readonly IRBuilder PreHeader, Header, Latch, Exit;
    public readonly IRBuilder Body;

    public readonly BasicBlock EntryBlock; //PreHeader may be forked and so we need to keep track of the actual entry block
    readonly List<(PhiInst HeadPhi, Instruction Next)> _pendingAccums = new();

    public LoopBuilder(BasicBlock blockInsertPos, string blockNamePrefix = "l_")
    {
        PreHeader = CreateBlock("PreHeader");
        Header = CreateBlock("Header");
        Body = CreateBlock("Body");
        Latch = CreateBlock("Latch");
        Exit = CreateBlock("Exit");

        EntryBlock = PreHeader.Block;

        IRBuilder CreateBlock(string name)
        {
            blockInsertPos = blockInsertPos.Method.CreateBlock(insertAfter: blockInsertPos);
            return new IRBuilder(blockInsertPos.SetName(blockNamePrefix + name));
        }
    }

    //PreHeader:
    //  ...
    //  goto Header
    //Header:
    //  var accumN = phi [PreHeader -> accumN_Seed, Latch -> accumN_Next]
    //  bool hasNext = ${emitBound()}
    //  goto hasNext ? Body : Exit
    //Body:
    //  ${emitBody()}
    //  goto Latch
    //Latch:
    //  var accumN_Next = update(accumN)
    //  goto Header
    //Exit:
    //  ...
    public void Build(
        Func<IRBuilder, Value> emitCond,
        Action<IRBuilder> emitBody)
    {
        var hasNext = emitCond(Header);
        Header.SetBranch(hasNext, Body.Block, Exit.Block);

        emitBody(Body);

        if (Body.Block.Last is not { IsBranch: true }) {
            Body.SetBranch(Latch.Block);
        }
        PreHeader.SetBranch(Header.Block);

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
    public PhiInst CreateAccum(Value seed, Func<Value, Value> emitUpdate)
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
    public PhiInst CreateInductor()
    {
        return CreateAccum(
            seed: ConstInt.CreateI(0),
            emitUpdate: curr => Latch.CreateAdd(curr, ConstInt.CreateI(1))
        ).SetName("index");
    }

    public void InsertBefore(Instruction inst)
    {
        var newBlock = inst.Block.Split(inst, branchTo: EntryBlock);
        Exit.SetBranch(newBlock);
    }
}