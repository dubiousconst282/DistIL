namespace DistIL.Analysis;

//https://llvm.org/docs/LoopTerminology.html
//https://pages.cs.wisc.edu/~fischer/cs701.f14/finding.loops.html

/// <summary> Finds natural loops in the CFG. </summary>
public class LoopAnalysis : IMethodAnalysis
{
    public List<LoopInfo> Loops { get; } = new();

    public LoopAnalysis(MethodBody method, DominatorTree domTree)
    {
        var worklist = new ArrayStack<BasicBlock>();

        foreach (var block in method) {
            foreach (var header in block.Succs) {
                //Check if `block -> header` is a backedge
                if (!domTree.Dominates(header, block)) continue;

                //The loop body includes `header`, `block`, and all predecessors
                //of `block` (direct and indirect) up to `header`.
                var body = new RefSet<BasicBlock>();
                body.Add(header);
                worklist.Push(block);

                while (worklist.TryPop(out var bodyBlock)) {
                    if (!body.Add(bodyBlock)) continue;

                    foreach (var pred in bodyBlock.Preds) {
                        worklist.Push(pred);
                    }
                }
                Loops.Add(new LoopInfo() {
                    Header = header,
                    Body = body,
                    Latch = block,
                    PreHeader = FindPreHeader(header, body, domTree)
                });
            }
        }
    }

    //TODO: maybe this should be done by the loop canocalization pass (so that new blocks can be created)
    private BasicBlock? FindPreHeader(BasicBlock header, RefSet<BasicBlock> body, DominatorTree domTree)
    {
        var preHeader = default(BasicBlock);
        int numFound = 0;
        foreach (var pred in header.Preds) {
            if (pred.NumSuccs == 1 && !body.Contains(pred) && domTree.Dominates(pred, header)) {
                preHeader = pred;
                numFound++;
            }
        }
        return numFound == 1 ? preHeader : null;
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new LoopAnalysis(mgr.Method, mgr.GetAnalysis<DominatorTree>(preserve: true));
}
public class LoopInfo
{
    public required BasicBlock Header { get; set; }
    public required RefSet<BasicBlock> Body { get; set; } 
    public required BasicBlock Latch { get; set; }

    public BasicBlock? PreHeader { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        if (PreHeader != null) sb.Append($"PreHeader={PreHeader} ");
        sb.Append($"Header={Header} Latch={Latch} Body=[");
        int i = 0;
        foreach (var block in Body) {
            if (i++ > 0) sb.Append(" ");
            sb.Append(block);
        }
        return sb.Append("]").ToString();
    }
}