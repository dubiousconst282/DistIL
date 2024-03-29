namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR.Utils;

public class PresizeLists : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>();
        var domTree = ctx.GetAnalysis<DominatorTree>();

        var candidateLists = new Dictionary<TrackedValue, (bool HasConditionalAdd, int AddCallCount)>();
        int numChanges = 0;

        foreach (var loop in loopAnalysis.GetShapedLoops(innermostOnly: true)) {
            // Must be able to calculate loop trip count before entering it
            if (!loop.HasKnownTripCount(domTree, loop.PreHeader.Last)) continue;

            // Find Add() calls inside loop
            foreach (var block in loop.Blocks) {
                foreach (var inst in block) {
                    if (!(inst is CallInst call && IsListAdd(call.Method))) continue;

                    // List must have been defined outside loop
                    if (call.Args is not [TrackedValue list, ..] || !loop.IsInvariant(list)) continue;

                    // List must not have been initialized with capacity
                    if (list is NewObjInst { Args: [{ ResultType.Kind: TypeKind.Int32 }] }) continue;

                    ref var info = ref candidateLists.GetOrAddRef(list);

                    // The call is executed unconditionally on every loop iteration iff
                    // the block it is defined in dominates the loop latch.
                    // We could probably speculate a reasonable initial capacity given
                    // PGO data, but that's thinking way ahead :')
                    if (domTree.Dominates(inst.Block, loop.Latch)) {
                        info.AddCallCount++;
                    } else {
                        info.HasConditionalAdd = true;
                    }
                }
            }

            // Pre-size lists
            foreach (var (list, info) in candidateLists) {
                if (info.AddCallCount == 0) continue; // nothing we can do

                var builder = new IRBuilder(loop.PreHeader);
                var numAddedItems = builder.CreateMul(loop.GetTripCount(builder)!, ConstInt.CreateI(info.AddCallCount));
                var newList = list;

                if (list is NewObjInst listAlloc && CanSinkAlloc(listAlloc, numAddedItems as Instruction ?? loop.PreHeader.Last, domTree)) {
                    Debug.Assert(listAlloc.Operands.Length == 0);

                    var ctorWithCap = list.ResultType.FindMethod(".ctor", new MethodSig(PrimType.Void, [PrimType.Int32]));
                    newList = builder.CreateNewObj(ctorWithCap, [numAddedItems]);
                    listAlloc.ReplaceWith(newList);
                } else {
                    var minCap = builder.CreateAdd(numAddedItems, builder.CreateFieldLoad("_size", list));
                    builder.CreateCallVirt("EnsureCapacity", [list, minCap]);
                }

                if (!info.HasConditionalAdd) {
                    InlineAddCalls(loop, builder, newList, numAddedItems);
                }
                numChanges++;
            }
            candidateLists.Clear();
        }
        return numChanges > 0 ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    // Given a list presized to the exact loop trip count, attempts to replace all Add() calls
    // inside the loop with direct array stores.
    // Also attempts to remove ToArray() calls and the list allocation.
    private static bool InlineAddCalls(ShapedLoopInfo loop, IRBuilder builder, TrackedValue list, Value numAddedItems)
    {
        var addCalls = new List<CallInst>();
        var toArrayCall = default(CallInst);
        int numToArrayCalls = 0, numOtherUses = 0;

        foreach (var user in list.Users()) {
            if (loop.Contains(user.Block)) {
                // Loop uses must be only from Add() calls
                if (user is CallInst call && IsListMethod(call.Method) && call.Method.Name == "Add") {
                    addCalls.Add(call);
                } else {
                    return false;
                }
            } else {
                // Uses from elsewhere can do whatever, we just need to track ToArray() calls
                if (user is CallInst call && IsListMethod(call.Method) && call.Method.Name == "ToArray") {
                    toArrayCall = call;
                    numToArrayCalls++;
                } else {
                    numOtherUses++;
                }
            }
        }

        Value? array;
        Value offset = ConstInt.CreateI(0);

        // If the list is created within the method, and there are no other uses
        // but the Add() calls and a single ToArray() outside the loop at the end,
        // we can completely remove the list alloc and fill an array directly.
        if (toArrayCall != null && numToArrayCalls == 1 && numOtherUses == 0 &&
            list is NewObjInst { Args: [{ ResultType.Kind: TypeKind.Int32 }] } listAlloc
        ) {
            var elemType = list.ResultType.GenericParams[0];
            array = builder.CreateNewArray(elemType, listAlloc.Args[0]).SetName("arrbuilder_items");

            toArrayCall.ReplaceWith(array);
            listAlloc.Remove();
            Debug.Assert(listAlloc.NumUses == addCalls.Count);
        } else {
            // TODO: centralize List<T> field accesses and consider using CM.SetCount() and AsSpan() instead
            array = builder.CreateFieldLoad("_items", list);
            offset = builder.CreateFieldLoad("_size", list);
            builder.CreateFieldStore("_size", list, builder.CreateAdd(offset, numAddedItems));
        }

        // Emitting a pointer increment for IEnumerables is a bit questionable because bad collections could
        // theoretically report a wrong count, and the perf delta is quite small (the cost of a bounds check).
        // |        ArrayIdx |    4 |     6.209 ns |   115 B |
        // |     ArrayPtrInc |    4 |     5.760 ns |    78 B |
        // |        ArrayIdx | 4096 | 4,095.651 ns |   115 B |
        // |     ArrayPtrInc | 4096 | 4,081.241 ns |    78 B |

        // With a little more effort we can avoid creating a non-SSA variable and
        // potentially enable vectorization by specializing over canonical for..i loops.
        if (loop is CountingLoopInfo { IsCanonical: true } forLoop) {
            var basePtr = LoopStrengthReduction.CreateGetDataPtrRange(builder, array, getCount: false).BasePtr;
            basePtr = builder.CreatePtrOffset(basePtr, offset);

            foreach (var call in addCalls) {
                builder.SetPosition(call, InsertionDir.After);

                var ptr = builder.CreatePtrOffset(basePtr, forLoop.Counter);
                builder.CreateStore(ptr, call.Args[1]);
                call.Remove();
            }
        } else {
            var idxVar = builder.Method.CreateVar(PrimType.Int32, "arrbuilder_idx");
            builder.CreateStore(idxVar, offset);

            foreach (var call in addCalls) {
                builder.SetPosition(call, InsertionDir.After);

                builder.CreateArrayStore(array, builder.CreateLoad(idxVar), call.Args[1]); // array[idx] = value
                builder.CreateStore(idxVar, builder.CreateAdd(builder.CreateLoad(idxVar), ConstInt.CreateI(1))); // idx++
                call.Remove();
            }
        }
        return true;
    }

    // Checks if `alloc` can be moved to after `inst`
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
        return method.Name == "Add" && IsListMethod(method);
    }
    private static bool IsListMethod(MethodDesc method)
    {
        return method.DeclaringType.IsCorelibType(typeof(List<>));
    }
}