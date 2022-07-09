namespace DistIL.Analysis;

using DistIL.IR;
using InstSet = ValueSet<IR.Instruction>;

/// <summary>
/// Liveness analysis for SSA definitions. The current implementation is based on the path exploration method.
/// 
/// See "Computing Liveness Sets for SSA-Form Programs" (https://hal.inria.fr/inria-00558509v2/document)
/// and section 7.4 of the SSA book.
/// </summary>
public class LivenessAnalysis : IMethodAnalysis
{
    readonly Dictionary<BasicBlock, (InstSet LiveIn, InstSet LiveOut)> _blockData = new();

    public LivenessAnalysis(MethodBody method)
    {
        //Init block sets
        foreach (var block in method) {
            _blockData[block] = (new InstSet(), new InstSet());
        }

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
                } else {
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
        bool AddLiveIn(BasicBlock block, Instruction inst) => _blockData[block].LiveIn.Add(inst);
        bool AddLiveOut(BasicBlock block, Instruction inst) => _blockData[block].LiveOut.Add(inst);
    }

    public static IMethodAnalysis Create(IMethodAnalysisManager mgr)
    {
        return new LivenessAnalysis(mgr.Method);
    }

    /// <summary> Returns the live sets for `block`. </summary>
    public (InstSet In, InstSet Out) GetLive(BasicBlock block) => _blockData[block];

    /// <summary> Checks if `inst` is live at the start of `block`. </summary>
    public bool IsLiveIn(BasicBlock block, Instruction inst) => _blockData[block].LiveIn.Contains(inst);

    /// <summary> Checks if `inst` is live when `block` exits. </summary>
    public bool IsLiveOut(BasicBlock block, Instruction inst) => _blockData[block].LiveOut.Contains(inst);

    public override string ToString()
    {
        var sb = new StringBuilder();
        var pc = new PrintContext(new System.IO.StringWriter(sb), _blockData.First().Key.Method.GetSymbolTable());

        foreach (var (block, (liveIn, liveOut)) in _blockData) {
            if (liveIn.Count + liveOut.Count == 0) continue;
            
            sb.Append($"{block}:\n");
            PrintSet("  In: [", liveIn);
            PrintSet("  Out: [", liveOut);

            void PrintSet(string prefix, InstSet set)
            {
                if (set.Count == 0) return;

                sb.Append(prefix);
                foreach (var inst in set) {
                    inst.PrintAsOperand(pc);
                    sb.Append(", ");
                }
                sb.Length -= 2;
                sb.Append("]\n");
            }
        }
        return sb.ToString();
    }
}