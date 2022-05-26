namespace DistIL.Passes;

using DistIL.AsmIO;
using DistIL.IR;

public abstract class Pass
{
}
public abstract class ModulePass : Pass
{
    public abstract void Transform(ModuleDef module);
}
public abstract class MethodPass : Pass
{
    public abstract void Transform(MethodBody method);
}