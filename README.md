# DistIL
![GitHub](https://img.shields.io/github/license/dubiousconst282/DistIL)
[![Nuget](https://img.shields.io/nuget/v/DistIL.OptimizerTask)](https://www.nuget.org/packages/DistIL.OptimizerTask)

Post-build IL optimizer and intermediate representation for .NET programs (experimental).

# Notable Features
- [SSA-based Intermediate Representation](./docs/api-walkthrough.md)
- Linq Expansion
- Loop Vectorization
- List Pre-sizing
- Lambda Devirtualization
- Method Inlining
- Scalar Replacement

See [Optimization Passes](./docs/opt-list.md) for a detailed list of optimizations passes available.

# Usage
Preview versions of the optimizer can be used by installing the [DistIL.OptimizerTask](https://www.nuget.org/packages/DistIL.OptimizerTask) NuGet package. It contains a MSBuild task which will automatically invoke the optimizer on the output project assembly when building in _Release_ mode.

By default, only methods and classes annotated with `[Optimize]` will be transformed, in order to reduce the chances of things breaking unexpectedly. This can be changed by setting the `DistilAllMethods` project property to `true`.
