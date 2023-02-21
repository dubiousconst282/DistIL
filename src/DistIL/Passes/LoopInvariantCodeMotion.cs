namespace DistIL.Passes;

using DistIL.Analysis;

public class LoopInvariantCodeMotion : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        //https://www.cs.cornell.edu/courses/cs6120/2020fa/lesson/5
        var loopAnalysis = ctx.GetAnalysis<LoopAnalysis>(preserve: true);
        var invariantInsts = new HashSet<Instruction>(); //can't use RefSet here, because order matters
        bool changed = false;

        foreach (var loop in loopAnalysis.Loops) {
            var preheader = loop.GetPreheader();
            if (preheader == null) continue; //nowhere to move invariants

            //Find all invariant instructions
            foreach (var block in loop.Blocks) {
                foreach (var inst in block) {
                    if (CanBeHoisted(inst)) {
                        invariantInsts.Add(inst);
                    }
                }
            }
            //Hoist invariant insts to the preheader
            foreach (var inst in invariantInsts) {
                inst.MoveBefore(preheader.Last);
            }

            //Cleanup for next itr
            if (invariantInsts.Count > 0) {
                invariantInsts.Clear();
                changed = true;
            }

            bool CanBeHoisted(Instruction inst)
            {
                //Only hoist a few select instructions, which have no side effects
                if (inst is not BinaryInst or UnaryInst || inst.HasSideEffects) return false;

                foreach (var oper in inst.Operands) {
                    if (!loop.IsInvariant(oper) && !(oper is Instruction operI && invariantInsts.Contains(operI))) return false;
                }
                return true;
            }
        }

        return changed ? MethodInvalidations.Loops : 0;
    }
}