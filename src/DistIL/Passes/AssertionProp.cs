namespace DistIL.Passes;

using DistIL.Analysis;

/// <summary> A simple dominator-based "assertion propagation" pass. </summary>
public class AssertionProp : IMethodPass
{
    // We mostly want this pass to eliminate null-checks/duplicated conditions and possibly
    // array range checks. These are quite few in typical methods, but it enables folding of
    // some LINQ dispatching code.
    //
    // Unfortunately, extending this impl to support array bounds-check elimination may be difficult since
    // the assert chain will not include info from other scopes of the dom tree, thus deriving accurate
    // range info from it might not be possible.
    //
    // Both GCC and LLVM use an on-demand approach to determine this kind of info, which looks relatively easy
    // to implement and could be faster as it would save some needless tracking:
    // - https://gcc.gnu.org/wiki/AndrewMacLeod/Ranger
    // - LLVM LazyValueInfo and ValueTracking
    //
    // SCCP is another option but it looks rather finicky, requiring insertion of "assertion" defs in the IR.
    // It would probably catch edge cases that this might miss?
    //
    // RyuJIT on the otherhand appears to be using path sensitive DFA to compute all asserts + a later elimination pass,
    // which also sounds complicated/expansive.

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var domTree = ctx.GetAnalysis<DominatorTree>();
        var impliedAsserts = new Dictionary<Value, Assertion>();
        bool changed = false;

        domTree.Traverse(preVisit: EnterBlock);

        return changed ? MethodInvalidations.DataFlow : MethodInvalidations.None;

        void EnterBlock(BasicBlock block)
        {
            // Add implications from predecessor branches.
            foreach (var pred in block.Preds) {
                if (pred.Last is not BranchInst { Cond: CompareInst cmp } br) continue;

                // Backedges must be ignored because otherwise they will lead to
                // incorrect folding of loop conditions:
                //   Header:
                //      (cond is implied to be true here, will be incorrectly folded at body)
                //   Body:
                //     cond = cmp ...
                //     if (cond) goto header;
                if (domTree.Dominates(block, pred)) continue;

                var op = block == br.Then ? cmp.Op : cmp.Op.GetNegated();
                Imply(block, op, cmp.Left, cmp.Right);

                // Replace existing uses of the predecessor branch condition's
                // that are dominated by this block, to catch cases like:
                //   if (cond) { if (cond) { ... } }
                if (block.NumPreds == 1) {
                    var condResult = ConstInt.CreateI(block == br.Then ? 1 : 0);

                    foreach (var use in cmp.Uses()) {
                        if (domTree.Dominates(block, use.Parent.Block)) {
                            use.Operand = condResult;
                            changed = true;
                        }
                    }
                }
            }

            foreach (var inst in block.NonPhis()) {
                // Access to an object implies that it must be non-null afterwards.
                // FIXME: check if try..catch regions will mess with this
                if (inst is MemoryInst or
                            CallInst { IsVirtual: true } or
                            FieldAddrInst { IsInstance: true } or
                            ArrayAddrInst
                    && inst.Operands[0] is TrackedValue obj
                ) {
                    Imply(block, CompareOp.Ne, obj, ConstNull.Create());
                    continue;
                }

                if (inst is CompareInst cmp && EvaluateCond(block, cmp.Op, cmp.Left, cmp.Right) is bool cond) {
                    cmp.ReplaceUses(ConstInt.CreateI(cond ? 1 : 0));
                    changed = true;
                    continue;
                }
            }
        }

        // Adds an assertion that implies a true condition.
        void Imply(BasicBlock block, CompareOp op, Value left, Value right)
        {
            if (EvaluateCond(block, op, left, right) is not null) return;

            for (int i = 0; i < 2; i++) {
                var operand = i == 0 ? left : right;

                // Don't bother tracking asserts related to consts for now
                if (operand is not TrackedValue) continue;

                ref var lastNode = ref impliedAsserts.GetOrAddRef(operand);
                lastNode = new Assertion() {
                    Block = block,
                    Op = op, Left = left, Right = right,
                    Prev = lastNode
                };
            }
        }

        bool? EvaluateCond(BasicBlock activeBlock, CompareOp op, Value left, Value right)
        {
            return Evaluate(GetActiveAssert(activeBlock, left), op, left, right) ??
                   Evaluate(GetActiveAssert(activeBlock, right), op, left, right);
        }
        Assertion? GetActiveAssert(BasicBlock activeBlock, Value key)
        {
            if (!impliedAsserts.ContainsKey(key)) return null;

            ref var node = ref impliedAsserts.GetRef(key);

            // Lazily remove asserts that are out of scope, rather than
            // tracking and removing dirty state on PostVisit.
            // This shouldn't be too slow since dom queries are O(1).
            while (node != null && !domTree.Dominates(node.Block, activeBlock)) {
                node = node.Prev;
            }

            return node;
        }
    }

    private static bool? Evaluate(Assertion? assert, CompareOp op, Value left, Value right)
    {
        for (; assert != null; assert = assert.Prev!) {
            bool argsMatch = assert.Left.Equals(left) && assert.Right.Equals(right);

            if (!argsMatch && assert.Left.Equals(right) && assert.Right.Equals(left)) {
                op = op.GetSwapped();
                argsMatch = true;
            }

            if (argsMatch) {
                if (AssertImpliesCond(assert.Op, op)) {
                    return true;
                }
                if (AssertImpliesCond(assert.Op, op.GetNegated())) {
                    return false;
                }
            }

            // TODO: evaluate relations, eg. x < 10  implies  x < 20
        }
        return null;
    }

    private static bool AssertImpliesCond(CompareOp assert, CompareOp cond)
    {
        // x < y  implies  x <= y
        if (assert.IsStrict() && !cond.IsStrict()) {
            cond = cond.GetStrict();
        }
        return assert == cond;
    }

    // List of known assertions linked to a value, ordered in most recent order.
    class Assertion
    {
        public required CompareOp Op;
        public required Value Left, Right;
        public required BasicBlock Block;
        public Assertion? Prev;

        public override string ToString()
        {
            var sw = new StringWriter();
            var symTable = (Left as TrackedValue ?? Right as TrackedValue)?.GetSymbolTable();
            var pc = new PrintContext(sw, symTable ?? SymbolTable.Empty);
            pc.Print($"{Op.ToString()} {Left}, {Right}");
            return sw.ToString();
        }
    }
}