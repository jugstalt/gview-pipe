using System.Reflection;

namespace gView.Pipe.Core.Options;

public class PipeCoreOptions
{
    /// <summary>
    /// Folder with external plugins, one sub folder per plugin containing <c>&lt;FolderName&gt;.dll</c>.
    /// Each plugin is loaded into its own AssemblyLoadContext. Relative paths are resolved against the current directory.
    /// </summary>
    public string? PluginPath { get; set; } = "plugins";

    /// <summary>Load <see cref="ApplicationPluginPattern"/> assemblies from the application folder (built-in plugins).</summary>
    public bool ScanApplicationDirectory { get; set; } = true;

    public string ApplicationPluginPattern { get; set; } = "gView.Pipe.Plugins*.dll";

    /// <summary>Additional assemblies to scan for nodes and credential types (already loaded, e.g. for tests).</summary>
    public List<Assembly> PluginAssemblies { get; } = new();

    /// <summary>
    /// Assemblies with these name prefixes are always taken from the host, never from a plugin folder,
    /// so plugin and host share the same contract types.
    /// </summary>
    public List<string> SharedAssemblyPrefixes { get; } = new()
    {
        "gView.Pipe.Abstraction",
        "Microsoft.Extensions.",
        "Microsoft.AspNetCore.",
        "System."
    };

    /// <summary>Binary content up to this size is kept in memory, larger content goes to temp files.</summary>
    public long BinaryMemoryThreshold { get; set; } = 1024 * 1024;

    /// <summary>Root for temp files of executions. Defaults to the system temp path.</summary>
    public string? TempPath { get; set; }

    /// <summary>Executions kept per workflow.</summary>
    public int ExecutionRetention { get; set; } = 50;

    /// <summary>Maximum nesting of sub-workflow calls (Execute Workflow), so recursion can't run forever.</summary>
    public int MaxSubWorkflowDepth { get; set; } = 10;

    /// <summary>Limits for JavaScript in expressions and Code nodes.</summary>
    public PipeScriptOptions Scripts { get; } = new();

    /// <summary>Arm the triggers of all active workflows when the host starts.</summary>
    public bool ActivateTriggersOnStartup { get; set; } = true;
}
