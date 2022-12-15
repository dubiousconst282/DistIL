namespace DistIL.Passes;

using DistIL.Analysis;

public abstract class Pass
{
}
public abstract class ModulePass : Pass
{
    public abstract void Run(ModuleTransformContext ctx);
}
public abstract class MethodPass : Pass
{
    public abstract void Run(MethodTransformContext ctx);
}

public class ModulePassManager
{
    public List<ModulePass> Pipeline { get; } = new();

    public void Add(ModulePass pass)
    {
        Pipeline.Add(pass);
    }

    public void Run(Compilation comp)
    {
        var callGraph = new CallGraph(comp.Module);
        var definedMethods = new List<MethodDef>(callGraph.NumMethods);
        callGraph.Traverse(postVisit: definedMethods.Add);

        foreach (var pass in Pipeline) {
            pass.Run(new ModuleTransformContext(comp, comp.Module, definedMethods));
        }
    }
}

public class MethodPassManager : ModulePass
{
    public List<MethodPass> Pipeline { get; } = new();

    public void Add(MethodPass pass)
    {
        Pipeline.Add(pass);
    }

    public override void Run(ModuleTransformContext ctx)
    {
        foreach (var method in ctx.DefinedMethods) {
            if (method.Body == null) continue;

            using var methodScope = ctx.Logger.Push(s_MethodScope, $"Applying passes to '{method}'");
            var methodCtx = new MethodTransformContext(ctx.Compilation, method.Body);

            foreach (var pass in Pipeline) {
                using var transformScope = ctx.Logger.Push(s_TransformScope, pass.GetType().Name);

                pass.Run(methodCtx);
            }
        }
    }

    static readonly LoggerScopeInfo s_MethodScope = new("DistIL.MethodPassManager");
    static readonly LoggerScopeInfo s_TransformScope = new("DistIL.TransformMethod");
}

public class MethodTransformContext : IMethodAnalysisManager
{
    public Compilation Compilation { get; }
    public MethodBody Method { get; }

    public MethodDef Definition => Method.Definition;
    public ModuleDef Module => Method.Definition.Module;

    public ICompilationLogger Logger => Compilation.Logger;
    
    readonly Dictionary<Type, (IMethodAnalysis Analysis, bool Valid)> _analyses = new();

    public MethodTransformContext(Compilation comp, MethodBody method)
    {
        Compilation = comp;
        Method = method;
    }

    public A GetAnalysis<A>(bool preserve = false) where A : IMethodAnalysis
    {
        Logger.Trace($"Get analysis {typeof(A).Name}");

        if (!preserve && !_analyses.ContainsKey(typeof(A))) {
            return (A)A.Create(this);
        }
        ref var info = ref _analyses.GetOrAddRef(typeof(A));
        if (info.Analysis == null || !info.Valid) {
            info.Analysis = A.Create(this);
        }
        info.Valid = preserve;
        return (A)info.Analysis;
    }
    public void InvalidateAll()
    {
        _analyses.Clear();
    }
}
public class ModuleTransformContext
{
    public Compilation Compilation { get; }
    public ModuleDef Module { get; }
    /// <summary> Methods defined in the module, in topological call order. </summary>
    public IReadOnlyCollection<MethodDef> DefinedMethods { get; }

    public ICompilationLogger Logger => Compilation.Logger;

    public ModuleTransformContext(Compilation comp, ModuleDef module, IReadOnlyCollection<MethodDef> definedMethods)
    {
        Compilation = comp;
        Module = module;
        DefinedMethods = definedMethods;
    }
}