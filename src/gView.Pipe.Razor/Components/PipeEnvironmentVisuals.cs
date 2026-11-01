using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Razor.Components;

/// <summary>Colors of environments: the closer to production, the warmer.</summary>
static public class PipeEnvironmentVisuals
{
    static public string Accent(string? environment) => environment?.ToUpperInvariant() switch
    {
        "PROD" or "PRODUCTION" => PipeGroupVisuals.Accent(PipeWorkflowGroupColors.Red),
        "STAGE" or "STAGING" => PipeGroupVisuals.Accent(PipeWorkflowGroupColors.Orange),
        "TEST" or "DEV" => PipeGroupVisuals.Accent(PipeWorkflowGroupColors.Blue),
        PipeEnvironments.Default or null => PipeGroupVisuals.Accent(PipeWorkflowGroupColors.Gray),
        _ => PipeGroupVisuals.Accent(PipeWorkflowGroupColors.Purple)
    };

    static public string Describe(PipeEffectiveEnvironment environment) => environment.Source switch
    {
        PipeEnvironmentSource.Workflow => $"Environment {environment.Name}, set on the workflow",
        PipeEnvironmentSource.Group => $"Environment {environment.Name}, inherited from group '{environment.SourceName}'",
        _ => $"Environment {environment.Name}, inherited from the instance"
    };
}
