using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;

namespace gView.Pipe.Plugins.Common;

static public class PipeExecutionOutput
{
    /// <summary>Items of the node that finished last: its main output, or all its outputs if it has no main port.</summary>
    static public IReadOnlyList<PipeItem> LastNodeItems(PipeExecution execution)
    {
        var last = execution.NodeRuns.Values
            .Where(r => r.Status == PipeNodeRunStatus.Success && r.Outputs is not null)
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefault();

        return last?.Outputs is { } outputs
            ? outputs.TryGetValue(PipePorts.Main, out var main) ? main : outputs.Values.SelectMany(i => i).ToList()
            : [];
    }
}
