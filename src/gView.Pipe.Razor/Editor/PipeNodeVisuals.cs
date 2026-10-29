using gView.Pipe.Abstraction.Nodes;
using MudBlazor;

namespace gView.Pipe.Razor.Editor;

/// <summary>Icon and accent color of a node, derived from its category.</summary>
static public class PipeNodeVisuals
{
    static public string Icon(PipeNodeDescriptor? descriptor)
        => descriptor is null
            ? Icons.Material.Filled.HelpOutline
            : descriptor.IsTrigger
                ? Icons.Material.Filled.Bolt
                : descriptor.Category switch
                {
                    "Network" => Icons.Material.Filled.Language,
                    "Transform" => Icons.Material.Filled.Edit,
                    "Flow" => Icons.Material.Filled.CallSplit,
                    "Utility" => Icons.Material.Filled.Build,
                    _ => Icons.Material.Filled.Extension
                };

    static public string Accent(PipeNodeDescriptor? descriptor)
        => descriptor is null
            ? "#d14343"
            : descriptor.IsTrigger
                ? "#e0902b"
                : descriptor.Category switch
                {
                    "Network" => "#3d6fd6",
                    "Transform" => "#7a5af5",
                    "Flow" => "#1f9d7a",
                    "Utility" => "#6b7280",
                    _ => "#2c8fb8"
                };
}
