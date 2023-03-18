using System;

namespace DistIL.Attributes
{
    /// <summary> Specifies that a class or method should be transformed by DistIL. </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class OptimizeAttribute : Attribute
    {
        /// <summary> Tries to vectorize simple for-loops. </summary>
        public bool TryVectorize { get; set; }
    }

    /// <summary> Specifies that a class or method should not be transformed by DistIL. </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class DoNotOptimizeAttribute : Attribute { }
}