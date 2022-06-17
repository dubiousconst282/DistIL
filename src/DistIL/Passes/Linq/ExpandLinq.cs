namespace DistIL.Passes;

using DistIL.IR;
using DistIL.Passes.Linq;

//See docs/Linq-Optimization.md for some notes.
public class ExpandLinq : MethodPass
{
    TypeDesc? t_Enumerable;

    public override void Run(MethodTransformContext ctx)
    {
        ctx.PreserveAll();

        t_Enumerable = ctx.Module.GetImport(typeof(Enumerable));
        if (t_Enumerable == null) return; //Module doesn't reference linq.

        //Find root queries
        var roots = default(List<QueryStage>);

        foreach (var inst in ctx.Method.Instructions()) {
            var stage = CreateStage(inst, onlyIfRoot: true);
            if (stage != null) {
                roots ??= new();
                roots.Add(stage);
            }
        }
        if (roots == null) return;

        foreach (var startStage in roots) {
            var endStage = CreatePipeline(startStage);
            if (endStage == null) {
                //TODO: proper logging
                Console.WriteLine($"Failed to create query pipeline: {ctx.Method} for start stage {startStage}");
                continue;
            }
            var synther = new QuerySynthesizer(ctx.Method, startStage, endStage);
            synther.Synth();
            synther.Replace();
        }
    }

    private QueryStage? CreateStage(Instruction inst, bool onlyIfRoot = false)
    {
        if (!(inst is CallInst call && call.Method.DeclaringType == t_Enumerable)) return null;
        if (!(call.IsStatic && call.NumArgs > 0)) return null;
        if (onlyIfRoot && !IsRoot(call)) return null;

        return QueryStage.Create(call);
    }
    private bool IsRoot(CallInst call)
    {
        //Query roots are linq calls whose input argument has a concrete IEnumerable type (Array, List, ...)
        var inputType = call.Args[0].ResultType;
        if (inputType is not ArrayType) return false;
        //TODO: handle more types
        return true;
    }

    /// <summary> Create links to the entire pipeline, and return the exit stage, or null on failure. </summary>
    private QueryStage? CreatePipeline(QueryStage root)
    {
        var currStage = root;
        while (true) {
            int numUses = currStage.Call.NumUses;
            if (numUses == 0 || currStage.IsExit()) {
                return currStage;
            }
            if (numUses >= 2) {
                return null; //We can't handle _forking_ queries
            }
            var nextStage = CreateStage(currStage.Call.GetFirstUser()!);
            if (nextStage == null) {
                return null;
            }
            currStage.Next = nextStage;
            nextStage.Prev = currStage;
            currStage = nextStage;
        }
    }
}