namespace DistIL.Optimizer;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using BuildTask = Microsoft.Build.Utilities.Task;

public class OptimizeTask : BuildTask
{
    public override bool Execute()
    {
        Log.LogMessage("It's working!");
        return true;
    }
}