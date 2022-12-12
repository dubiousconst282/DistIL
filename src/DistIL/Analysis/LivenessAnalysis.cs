namespace DistIL.Analysis;

using InstSet = RefSet<Instruction>;

/// <summary>
/// Liveness analysis for SSA definitions. The current implementation is based on the 
/// path exploration method, which has a complexity of O(numGlobalVars * numCrossedBlocks).
/// 
/// See "Computing Liveness Sets for SSA-Form Programs" (https://hal.inria.fr/inria-00558509v2/document)
/// and section 7.4 of the SSA book.
/// </summary>
public class LivenessAnalysis : IMethodAnalysis
{
    readonly Dictionary<BasicBlock, (InstSet? In, InstSet? Out)> _liveSets = new();

    public LivenessAnalysis(MethodBody method)
    {
        var worklist = new ArrayStack<BasicBlock>();

        //Visit all instructions defining a value
        foreach (var inst in method.Instructions()) {
            if (!inst.HasResult) continue;

            foreach (var user in inst.Users()) {
                if (user is PhiInst phi) {
                    //Enqueue predecessors for source blocks of this phi
                    foreach (var (pred, val) in phi) {
                        if (val == inst) {
                            worklist.Push(pred);
                            AddLiveOut(pred, inst);
                            AddLiveIn(user.Block, inst);
                        }
                    }
                } else if (user.Block != inst.Block) {
                    worklist.Push(user.Block);
                }
                //Traverse the CFG backwards to propagate liveness
                while (worklist.TryPop(out var userBlock)) {
                    if (inst.Block == userBlock) continue; //Reached the defining block
                    if (!AddLiveIn(userBlock, inst)) continue; //Already propagated

                    foreach (var pred in userBlock.Preds) {
                        AddLiveOut(pred, inst);
                        worklist.Push(pred);
                    }
                }
            }
        }

        //We could avoid set lookups by keeping the latest added block in _blockData,
        //but that's not a huge deal.
        bool AddLiveIn(BasicBlock block, Instruction inst)
            => (_liveSets.GetOrAddRef(block).In ??= new()).Add(inst);

        bool AddLiveOut(BasicBlock block, Instruction inst)
            => (_liveSets.GetOrAddRef(block).Out ??= new()).Add(inst);
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new LivenessAnalysis(mgr.Method);

    /// <summary> Returns the live sets for `block`. </summary>
    public (InstSet? In, InstSet? Out) GetLiveSets(BasicBlock block) => _liveSets.GetValueOrDefault(block);

    /// <summary> Checks if `inst` is live at the start of `block`. </summary>
    public bool IsLiveIn(BasicBlock block, Instruction inst) => GetLiveSets(block).In?.Contains(inst) ?? false;

    /// <summary> Checks if `inst` is live when `block` exits. </summary>
    public bool IsLiveOut(BasicBlock block, Instruction inst) => GetLiveSets(block).Out?.Contains(inst) ?? false;

    /// <summary> Checks if `inst` is live after `pos` executes. </summary>
    public bool IsLiveAfter(Instruction inst, Instruction pos)
    {
        if (IsLiveOut(pos.Block, inst)) {
            return true;
        }
        //If `a` is defined or liveIn in the same block as `b`, we need to check if it is used after it
        if (inst.Block == pos.Block || IsLiveIn(pos.Block, inst)) {
            for (var curr = pos; (curr = curr.Next) != null;) {
                if (curr.Operands.ContainsRef(inst)) {
                    return true;
                }
            }
        }
        return false;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        var pc = new PrintContext(new StringWriter(sb), _liveSets.First().Key.Method.GetSymbolTable());

        foreach (var (block, (liveIn, liveOut)) in _liveSets) {
            if (liveIn?.Count + liveOut?.Count == 0) continue;
            
            sb.Append($"{block}:\n");
            pc.PrintSequence("   In: [", "]", liveIn!.GetEnumerator().AsEnumerable(), pc.PrintAsOperand);
            pc.PrintSequence("  Out: [", "]", liveOut!.GetEnumerator().AsEnumerable(), pc.PrintAsOperand);
        }
        return sb.ToString();
    }
}