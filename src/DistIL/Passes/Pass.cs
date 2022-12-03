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

    public void Run(ModuleDef module)
    {
        var callGraph = new CallGraph(module);
        var definedMethods = new List<MethodDef>(callGraph.NumMethods);
        callGraph.Traverse(postVisit: definedMethods.Add);

        foreach (var pass in Pipeline) {
            pass.Run(new ModuleTransformContext(module, definedMethods));
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

            var methodCtx = new MethodTransformContext(method.Body);
            foreach (var pass in Pipeline) {
                pass.Run(methodCtx);
            }
        }
    }
}

public class MethodTransformContext : IMethodAnalysisManager
{
    public MethodBody Method { get; }
    public MethodDef Definition => Method.Definition;
    public ModuleDef Module => Method.Definition.Module;
    
    readonly Dictionary<Type, (IMethodAnalysis Analysis, bool Valid)> _analyses = new();

    public MethodTransformContext(MethodBody method)
    {
        Method = method;
    }

    public A GetAnalysis<A>(bool preserve = false) where A : IMethodAnalysis
    {
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
    public ModuleDef Module { get; }
    /// <summary> Methods defined in the module, in topological call order. </summary>
    public IReadOnlyCollection<MethodDef> DefinedMethods { get; }

    public ModuleTransformContext(ModuleDef module, IReadOnlyCollection<MethodDef> definedMethods)
    {
        Module = module;
        DefinedMethods = definedMethods;
    }
}