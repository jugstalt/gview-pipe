using gView.Pipe.Abstraction.Workflows;

namespace gView.Pipe.Razor.Components;

/// <summary>Accent colors of workflow groups; mid tones that read well on light and dark surfaces.</summary>
static public class PipeGroupVisuals
{
    static public string Accent(string? color) => color switch
    {
        PipeWorkflowGroupColors.Teal => "#1f9d9a",
        PipeWorkflowGroupColors.Green => "#2f9e44",
        PipeWorkflowGroupColors.Amber => "#d4a20b",
        PipeWorkflowGroupColors.Orange => "#e0782b",
        PipeWorkflowGroupColors.Red => "#d14343",
        PipeWorkflowGroupColors.Purple => "#7a5af5",
        PipeWorkflowGroupColors.Gray => "#7b8494",
        _ => "#3d6fd6"
    };
}
