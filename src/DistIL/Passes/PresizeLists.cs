namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR.Utils;

// ArrayBuilderOpts
public class PresizeLists : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>();
        var domTree = ctx.GetAnalysis<DominatorTree>();

        var candidateLists = new RefSet<Value>();
        int numChanges = 0;

        foreach (var loop in loopAnalysis.GetShapedLoops(innermostOnly: true)) {
            // Must be able to calculate loop trip count before entering it
            if (!loop.HasKnownTripCount(domTree, loop.PreHeader.Last)) continue;

            // Find Add() calls inside loop
            foreach (var block in loop.Blocks) {
                foreach (var inst in block) {
                    if (!(inst is CallInst call && IsListAdd(call.Method))) continue;

                    // List must have been created outside loop
                    if (!loop.Loop.IsInvariant(call.Args[0])) continue;

                    // List must not have been initialized with capacity
                    if (call.Args[0] is NewObjInst { Args: [ _, .. ] }) continue;

                    // Call must be executed unconditionally on every iteration
                    if (!domTree.Dominates(inst.Block, loop.Latch)) continue;

                    candidateLists.Add(call.Args[0]);
                }
            }

            // Pre-size lists
            foreach (var list in candidateLists) {
                Console.WriteLine(ctx.Method);

                var builder = new IRBuilder(loop.PreHeader);
                // TODO: multiply minSize with number of add calls inside loop
                var minSize = loop.GetTripCount(builder)!;

                if (list is NewObjInst listAlloc && minSize is Instruction minSizeI && CanSinkAlloc(listAlloc, minSizeI, domTree)) {
                    Debug.Assert(listAlloc.Operands.Length == 0);

                    var ctorWithCap = list.ResultType.FindMethod(".ctor", new MethodSig(PrimType.Void, [PrimType.Int32]));
                    listAlloc.ReplaceWith(builder.CreateNewObj(ctorWithCap, [minSizeI]));
                } else {
                    var minCap = builder.CreateAdd(minSize, builder.CreateCallVirt("get_Count", list));
                    builder.CreateCallVirt("EnsureCapacity", [list, minCap]);
                }
                numChanges++;
            }
            candidateLists.Clear();
        }
        return numChanges > 0 ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    private static bool CanSinkAlloc(NewObjInst alloc, Instruction inst, DominatorTree domTree)
    {
        foreach (var user in alloc.Users()) {
            if (!domTree.Dominates(inst, user)) {
                return false;
            }
        }
        return true;
    }

    private static bool IsListAdd(MethodDesc method)
    {
        // TODO: support for ImmutableArray builders
        return method.Name == "Add" &&
               method.DeclaringType.IsCorelibType(typeof(List<>));
    }
}