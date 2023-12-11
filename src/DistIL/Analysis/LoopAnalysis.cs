namespace DistIL.Analysis;

/// <summary> Finds natural loops in the CFG. </summary>
public class LoopAnalysis : IMethodAnalysis
{
    /// <summary> List of outer-most loops. </summary>
    public List<LoopInfo> Loops { get; } = new();

    public LoopAnalysis(MethodBody method, DominatorTree domTree)
    {
        // Based on https://pages.cs.wisc.edu/~fischer/cs701.f14/finding.loops.html
        var worklist = new ArrayStack<BasicBlock>();

        foreach (var header in method) {
            foreach (var latch in header.Preds) {
                // Check if `latch -> header` is actually a back-edge
                if (!domTree.Dominates(header, latch)) continue;

                // The loop body includes the header, latch, and all
                // predecessors from the latch up to the header.
                var body = new RefSet<BasicBlock>();
                body.Add(header);
                worklist.Push(latch);

                while (worklist.TryPop(out var block)) {
                    if (!body.Add(block)) continue;

                    foreach (var pred in block.Preds) {
                        worklist.Push(pred);
                    }
                }
                Loops.Add(new LoopInfo() {
                    Header = header,
                    Blocks = body
                });
            }
            // TODO: build loop tree
        }
    }

    public IEnumerable<ShapedLoopInfo> GetShapedLoops(bool innermostOnly = false)
        => (innermostOnly ? GetInnermostLoops() : Loops).Select(ShapedLoopInfo.Parse).Where(s => s != null)!;

    public IEnumerable<LoopInfo> GetInnermostLoops()
        => Loops; // FIXME: proper impl once we have loop trees

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new LoopAnalysis(mgr.Method, mgr.GetAnalysis<DominatorTree>());
}

/// <summary>
/// Represents the structure of a loop in a CFG.
/// See https://llvm.org/docs/LoopTerminology.html
/// </summary>
public class LoopInfo
{
    public required BasicBlock Header { get; init; }
    public required RefSet<BasicBlock> Blocks { get; init; }

    public LoopInfo? Parent { get; set; }
    internal LoopInfo? _firstChild, _nextSibling;

    public bool HasChildren => _firstChild != null;
    public int NumBlocks => Blocks.Count;

    /// <summary> Checks if the specified block is part of this loop. </summary>
    /// <remarks> This includes the header, latch, and any other body block. </remarks>
    public bool Contains(BasicBlock block) => Blocks.Contains(block);

    /// <summary> Checks if the specified value is defined outside the loop. </summary>
    public bool IsInvariant(Value val)
    {
        return !(val is Instruction inst && Contains(inst.Block));
    }

    public BasicBlock? GetPreheader()
    {
        var pred = GetPredecessor();
        return pred?.Last is BranchInst { IsJump: true } ? pred : null;
    }

    /// <summary> Returns the unique block entering the loop (its predecessor). Differently from the pre-header, this block may have multiple successors. </summary>
    public BasicBlock? GetPredecessor() => GetUniquePredAround(Header, inside: false);

    /// <summary> Returns the unique block with a back-edge to the header. </summary>
    public BasicBlock? GetLatch() => GetUniquePredAround(Header, inside: true);

    /// <summary> Returns the unique block that executes after the loop exits. </summary>
    public BasicBlock? GetExit()
    {
        var exit = default(BasicBlock);
        int count = 0;

        foreach (var block in Blocks) {
            foreach (var succ in block.Succs) {
                if (!Contains(succ) && succ != exit) {
                    exit = succ;
                    count++;
                }
            }
        }
        return count == 1 ? exit : null;
    }

    /// <summary> Returns the unique condition controlling the loop exit. </summary>
    public Instruction? GetExitCondition()
    {
        // Header: goto cmp ? Body : Exit
        var exit = GetExit();
        return exit != null && GetUniquePredAround(exit, inside: true) == Header &&
               Header.Last is BranchInst { Cond: Instruction cond } br &&
               br.Else == exit
            ? cond : null;
    }

    public IEnumerable<LoopInfo> GetChildren()
    {
        for (var child = _firstChild; child != null; child = child._nextSibling) {
            yield return child;
        }
    }

    // Returns the unique predecessor of `block` that is either inside or outside the loop.
    private BasicBlock? GetUniquePredAround(BasicBlock block, bool inside)
    {
        var result = default(BasicBlock);
        int count = 0;

        foreach (var pred in block.Preds) {
            if (Contains(pred) == inside) {
                result = pred;
                count++;
            }
        }
        return count == 1 ? result : null;
    }

    public override string ToString()
    {
        // Prehdr^ -> Header[B1 B2 B3 Exiting* Latch↲]
        var sb = new StringBuilder();

        var preheader = GetPreheader();
        var preds = Header.Preds.AsEnumerable().Where(b => !Contains(b));
        sb.AppendSequence(preds, PrintBlock, prefix: "", postfix: " -> ", separator: " ");

        PrintBlock(Header);

        var bodyBlocks = Blocks.GetEnumerator().AsEnumerable().Where(b => b != Header);
        sb.AppendSequence(bodyBlocks, PrintBlock, separator: " ");

        return sb.ToString();

        void PrintBlock(BasicBlock block)
        {
            sb.Append(block);

            if (block == preheader) {
                sb.Append('^');
                return;
            }
            bool isLatch = block.Succs.Contains(Header);
            bool isExiting = block.Succs.Any(b => !Contains(b));

            if (isLatch) sb.Append('↲');
            if (isExiting) sb.Append('*');
        }
    }
}