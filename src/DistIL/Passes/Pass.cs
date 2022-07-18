namespace DistIL.Passes;

using DistIL.AsmIO;
using DistIL.IR;
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
        foreach (var pass in Pipeline) {
            pass.Run(new ModuleTransformContext(module));
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
        foreach (var method in ctx.Module.AllMethods()) {
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
    
    private Dictionary<Type, (object/*IMethodAnalysis*/ Analysis, bool Valid)> _analysis = new();

    public MethodTransformContext(MethodBody method)
    {
        Method = method;
    }

    public A GetAnalysis<A>(bool preserve = false) where A : IMethodAnalysis
    {
        ref var info = ref _analysis.GetOrAddRef(typeof(A));
        if (info.Analysis == null || !info.Valid) {
            info.Analysis = A.Create(this);
        }
        info.Valid = preserve;
        return (A)info.Analysis;
    }
    public void Preserve<A>() where A : IMethodAnalysis
    {
        ref var info = ref _analysis.GetOrAddRef(typeof(A));
        info.Valid = true;
    }
    public void PreserveAll()
    {
        foreach (var key in _analysis.Keys) {
            _analysis.GetOrAddRef(key).Valid = true;
        }
    }
    public void InvalidateAll()
    {
        _analysis.Clear();
    }
}
public class ModuleTransformContext
{
    public ModuleDef Module { get; }

    public ModuleTransformContext(ModuleDef module)
    {
        Module = module;
    }
}