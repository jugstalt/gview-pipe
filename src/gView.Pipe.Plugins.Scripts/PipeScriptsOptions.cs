using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace gView.Pipe.Plugins.Scripts;

/// <summary>
/// What the host releases for running scripts; read from the configuration section <c>Pipe:Scripts</c>
/// (or set with <c>services.Configure&lt;PipeScriptsOptions&gt;(…)</c>). Nothing is runnable without it.
/// </summary>
public sealed class PipeScriptsOptions
{
    /// <summary>
    /// Folders with scripts, by name. Keep them apart from the file roots workflows can write to (Write File),
    /// otherwise a workflow could write a script and run it. Relative paths are resolved against the current directory.
    /// </summary>
    public Dictionary<string, string> ScriptRoots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Git repositories with scripts, by name.</summary>
    public Dictionary<string, PipeScriptRepository> Repositories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where repositories are checked out, one folder per name.</summary>
    public string RepositoriesPath { get; set; } = Path.Combine("data", "script-repos");

    /// <summary>Interpreter per file extension, e.g. <c>".py" → "C:\Python313\python.exe"</c>; overrides the defaults.</summary>
    public Dictionary<string, string> Interpreters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The git command line.</summary>
    public string GitPath { get; set; } = "git";
}

public sealed class PipeScriptRepository
{
    /// <summary>HTTPS URL (or a local path, e.g. for tests).</summary>
    public string Url { get; set; } = "";

    public string Branch { get; set; } = "main";

    /// <summary>Basic Auth (user + password or personal access token) or Bearer Token credential, for private repositories.</summary>
    public string? CredentialId { get; set; }
}

public sealed class PipeScriptsPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // bound when there is a configuration (hosts); tests configure the options directly
        services.AddOptions<PipeScriptsOptions>().Configure<IServiceProvider>((options, provider) =>
        {
            if (provider.GetService<IConfiguration>() is { } configuration)
            {
                configuration.GetSection("Pipe:Scripts").Bind(options);
            }
        });
        services.TryAddSingleton<PipeScriptRepositories>();
    }
}

/// <summary>Resolves paths inside a base folder, never outside of it.</summary>
static internal class PipeScriptPaths
{
    static public string Inside(string baseFolder, string? relativePath, string what)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseFolder));
        var relative = (relativePath ?? "").Trim().Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.StartsWith('/'))
        {
            throw new PipeException($"'{relativePath}' must be relative to the {what}");
        }

        var full = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(root, comparison) && !full.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            throw new PipeException($"'{relativePath}' leaves the {what}");
        }
        return full;
    }

    /// <summary>Path relative to the base folder with forward slashes, as stored in the node's script list.</summary>
    static public string Relative(string baseFolder, string fullPath)
        => Path.GetRelativePath(baseFolder, fullPath).Replace('\\', '/');
}
