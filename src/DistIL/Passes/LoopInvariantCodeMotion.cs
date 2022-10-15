namespace DistIL.Passes;

using DistIL.Analysis;
public class LoopInvariantCodeMotion : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        //https://www.cs.cornell.edu/courses/cs6120/2020fa/lesson/5
        var domTree = ctx.GetAnalysis<DominatorTree>(preserve: true);
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        var invariantInsts = new HashSet<Instruction>(); //can't use RefSet here, because order matters

        foreach (var loop in loopAnalysis.Loops) {
            if (loop.PreHeader == null) continue; //nowhere to move invariants

            //Find all invariant instructions
            foreach (var block in loop.Body) {
                foreach (var inst in block) {
                    if (CanBeHoisted(inst)) {
                        invariantInsts.Add(inst);
                    }
                }
            }
            //Hoist invariant insts to the preheader
            foreach (var inst in invariantInsts) {
                inst.MoveBefore(loop.PreHeader.Last);
            }
            //Cleanup for next itr
            invariantInsts.Clear();

            bool CanBeHoisted(Instruction inst)
            {
                //Only hoist a few select instructions, which have no side effects
                if (inst is not BinaryInst or UnaryInst || inst.HasSideEffects) return false;

                foreach (var oper in inst.Operands) {
                    if (!IsInvariant(oper)) return false;
                }
                return true;
            }
            bool IsInvariant(Value val)
            {
                return val is Argument or Const ||
                       (val is Instruction inst && (
                            domTree.Dominates(inst.Block, loop.PreHeader) || //defined before loop
                            invariantInsts.Contains(inst)                    //already marked as invariant
                        ));
            }
        }
    }
}