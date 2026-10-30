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

    /// <summary>Git servers whose repositories Run Scripts Repo may use, by name (e.g. a Gitea organization).</summary>
    public Dictionary<string, PipeGitServer> GitServers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where repositories are cloned for a run, one temporary folder per run (deleted afterwards).</summary>
    public string RepositoriesPath { get; set; } = Path.Combine("data", "script-repos");

    /// <summary>Interpreter per file extension, e.g. <c>".py" → "C:\Python313\python.exe"</c>; overrides the defaults.</summary>
    public Dictionary<string, string> Interpreters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The git command line.</summary>
    public string GitPath { get; set; } = "git";
}

static public class PipeGitProviders
{
    public const string Gitea = "gitea";
    public const string GitHub = "github";
}

/// <summary>A Git server and the owner (organization or user) whose repositories are offered.</summary>
public sealed class PipeGitServer
{
    /// <summary><see cref="PipeGitProviders.Gitea"/> or <see cref="PipeGitProviders.GitHub"/>; decides the API for listing.</summary>
    public string Provider { get; set; } = PipeGitProviders.Gitea;

    /// <summary>Base URL, e.g. <c>http://localhost:3000</c> or <c>https://github.com</c>.</summary>
    public string Url { get; set; } = "";

    /// <summary>Organization or user.</summary>
    public string Owner { get; set; } = "";

    /// <summary>Basic Auth (user + password or personal access token) or Bearer Token credential.</summary>
    public string? CredentialId { get; set; }

    /// <summary>Regex; only repositories whose name matches are offered (e.g. <c>^ops-</c>).</summary>
    public string? RepositoryFilter { get; set; }
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
        services.TryAddSingleton<PipeGitServers>();
        services.AddHttpClient(PipeGitServers.HttpClientName);
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
