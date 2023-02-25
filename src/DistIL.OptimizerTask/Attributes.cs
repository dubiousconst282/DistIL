using System;

namespace DistIL.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class OptimizeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class DoNotOptimizeAttribute : Attribute { }
}