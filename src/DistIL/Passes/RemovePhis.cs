namespace DistIL.Passes;

using DistIL.IR;

public class RemovePhis : MethodPass
{
    public override void Transform(Method method)
    {
        var phis = new List<PhiInst>();
        foreach (var block in method) {
            foreach (var phi in block.Phis()) {
                phis.Add(phi);
            }
        }

        foreach (var phi in phis) {
            var tempVar = new Variable(phi.ResultType);

            foreach (var (block, value) in phi) {
                if (value is Undef) continue;
                
                var store = new StoreVarInst(tempVar, value);

                if (value is Instruction inst) {
                    store.InsertAfter(inst);
                } else {
                    store.InsertBefore(block.Last);
                }
            }
            var load = new LoadVarInst(tempVar);
            phi.ReplaceWith(load);
        }

        foreach (var phi in phis) {
            phi.Remove();
        }
    }
}