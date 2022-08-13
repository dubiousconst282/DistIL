namespace DistIL.Passes;

using DistIL.IR;
using DistIL.Passes.Linq;

//TODO: should we handle consumed/iterated queries?
//Although a specialized transform would be more effective, general passes like
//method inlining, lambda inlining and SROA would probably give most of this optimization
//for free, so it may not be worth pursuing it too far. (We'd probably need to invest in 
//other complicated passes to handle state machines and weird CFGs.)
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
            ctx.InvalidateAll();
        }
    }
    //Original Code:
    //  int[] ArrayTransform(int[] arr) {
    //      return arr.Where(x => x > 0)
    //              .Select(x => x * 2)
    //              .ToArray();
    //  }
    //Lowered:
    //  class _PrivData {
    //      ...
    //      static bool Pred(int x) => x > 0; 
    //      static int Mapper(int x) => x * 2;
    //  }
    //  int[] ArrayTransform_Lowered(int[] arr) {
    //      var pred = _PrivData.PredCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Pred);
    //      var stage1 = Enumerable.Where(arr, pred);
    //      var mapper = _PrivData.MapperCache ??= new Func<int, bool>(_PrivData.Instance, &_PrivData.Mapper);
    //      var stage2 = Enumerable.Select(stage1, mapper);
    //      return stage2.ToArray();
    //  }
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