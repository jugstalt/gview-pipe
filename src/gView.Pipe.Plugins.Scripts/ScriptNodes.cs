using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Scripts;

/// <summary>Ids of the script nodes. Stored in workflows: never change them.</summary>
static public class PipeScriptNodes
{
    public const string RunScript = "33c3fd33-a8d4-44af-958a-e78b03b110b9";

    /// <summary>The id of the former "Run Scripts" node (folder or repository), version 2 is folder only.</summary>
    public const string RunScriptsFolder = "2f3f6f81-f55e-4b5c-87c1-59632ef03445";

    public const string RunScriptsRepo = "a879a716-ad50-4427-90eb-95eb04d32a14";
}

// ---------------------------------------------------------------- Run Script

public sealed class RunScriptNodeParameters : PipeScriptCommonParameters
{
    [PipeOptions]
    [PipeParameter(Label = "Script folder", Order = 1, Expressions = false, Description = "A folder released by the host")]
    public string? Root { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Script", Order = 2, Expressions = false)]
    public string? Script { get; set; }
}

[PipeNode(PipeScriptNodes.RunScript, "Run Script",
    Category = "Scripts",
    Description = "Runs one .bat/.cmd, PowerShell, Python or shell script from a folder released by the host, once per input item.")]
public sealed class RunScriptNode : IPipeNode<RunScriptNodeParameters>, IPipeNodeHints, IPipeParameterOptions
{
    private readonly PipeScriptsOptions _options;

    public RunScriptNode(IOptions<PipeScriptsOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var results = new List<PipeItem>();

        for (var i = 0; i < items.Count; i++)
        {
            var parameters = context.GetParameters<RunScriptNodeParameters>(items[i], i);
            if (string.IsNullOrWhiteSpace(parameters.Script))
            {
                throw new PipeException("Select a script");
            }

            var root = PipeScriptRoots.Resolve(_options, parameters.Root);
            var path = PipeScriptPaths.Inside(root, parameters.Script, "script folder");
            var execution = await PipeScriptExecution.CreateAsync(context, parameters, items[i], _options, null, cancellationToken);
            results.Add(await execution.RunAsync(path, parameters.Script, Path.GetDirectoryName(path)!, null, cancellationToken));
            if (!PipeScriptExecution.Succeeded(results[^1]))
            {
                break;
            }
        }
        return PipeScriptExecution.Result(results);
    }

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
        => _options.ScriptRoots.Count == 0
            ? [PipeScriptRoots.Warning, new(PipeNodeHintLevel.Warning, "No script folders are released (Pipe:Scripts:ScriptRoots).")]
            : [PipeScriptRoots.Warning, PipeScriptRoots.Runnable];

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
    {
        IReadOnlyList<PipeParameterOption> options = parameter switch
        {
            "root" => PipeScriptRoots.Options(_options),
            "script" when parameters["root"]?.GetValue<string>() is { Length: > 0 } root
                => PipeScriptRoots.FindScripts(PipeScriptRoots.Resolve(_options, root), null, includeSubfolders: true).Select(s => new PipeParameterOption(s)).ToList(),
            _ => []
        };
        return Task.FromResult(options);
    }
}

// ---------------------------------------------------------------- Run Scripts Folder

public sealed class RunScriptsFolderNodeParameters : PipeScriptCommonParameters
{
    [PipeOptions]
    [PipeParameter(Label = "Script folder", Order = 1, Expressions = false, Description = "A folder released by the host")]
    public string? Root { get; set; }

    [PipeParameter(Label = "Path", Order = 2, Expressions = false, Placeholder = "nightly/ or nightly/backup.ps1",
        Description = "File or folder inside the script folder; empty for all of it")]
    public string? Path { get; set; }

    [PipeParameter(Label = "Include subfolders", Order = 3, Expressions = false)]
    public bool IncludeSubfolders { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Scripts", Order = 4, Expressions = false,
        Description = "Run top to bottom, each in its own process; files added later are off until switched on")]
    public List<PipeSelection> Scripts { get; set; } = new();

    [PipeParameter(Label = "Stop on first failure", Order = 5, Expressions = false)]
    public bool StopOnFirstFailure { get; set; } = true;
}

[PipeNode(PipeScriptNodes.RunScriptsFolder, "Run Scripts Folder",
    Category = "Scripts",
    Version = 2,
    Description = "Runs the switched-on scripts of a folder released by the host, in order, once per input item. "
                  + "One output item per script with exit code and output.")]
public sealed class RunScriptsFolderNode : IPipeNode<RunScriptsFolderNodeParameters>, IPipeNodeHints, IPipeParameterOptions, IPipeNodeMigration
{
    private readonly PipeScriptsOptions _options;

    public RunScriptsFolderNode(IOptions<PipeScriptsOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        // version 1 could use a Git repository; that is Run Scripts Repo now
        if (context.RawParameters["source"]?.GetValue<string>() == "gitRepository")
        {
            throw new PipeException("Git repositories moved to the node Run Scripts Repo; replace this node with it");
        }

        var items = context.GetInput();
        var results = new List<PipeItem>();
        var stop = false;

        for (var i = 0; i < items.Count && !stop; i++)
        {
            var parameters = context.GetParameters<RunScriptsFolderNodeParameters>(items[i], i);
            var root = PipeScriptRoots.Resolve(_options, parameters.Root);
            var scripts = parameters.Scripts.Where(s => s.Enabled).Select(s => s.Value).ToList();
            if (scripts.Count == 0)
            {
                throw new PipeException("No script is switched on");
            }

            var execution = await PipeScriptExecution.CreateAsync(context, parameters, items[i], _options, null, cancellationToken);
            foreach (var script in scripts)
            {
                var path = PipeScriptPaths.Inside(root, script, "script folder");
                results.Add(await execution.RunAsync(path, script, System.IO.Path.GetDirectoryName(path)!, null, cancellationToken));
                if (!PipeScriptExecution.Succeeded(results[^1]) && parameters.StopOnFirstFailure)
                {
                    stop = true;
                    break;
                }
            }
        }
        return PipeScriptExecution.Result(results);
    }

    /// <summary>Version 1 had a source (folder or Git); folder nodes just lose the Git fields.</summary>
    public JsonObject MigrateParameters(int fromVersion, JsonObject parameters)
    {
        if (fromVersion < 2 && parameters["source"]?.GetValue<string>() != "gitRepository")
        {
            parameters.Remove("source");
            parameters.Remove("repository");
            parameters.Remove("useLastCheckoutIfPullFails");
        }
        return parameters;
    }

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
        => _options.ScriptRoots.Count == 0
            ? [PipeScriptRoots.Warning, new(PipeNodeHintLevel.Warning, "No script folders are released (Pipe:Scripts:ScriptRoots).")]
            : [PipeScriptRoots.Warning, PipeScriptRoots.Runnable];

    public Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
    {
        IReadOnlyList<PipeParameterOption> options = [];
        if (parameter == "root")
        {
            options = PipeScriptRoots.Options(_options);
        }
        else if (parameter == "scripts" && PipeJson.Deserialize<RunScriptsFolderNodeParameters>(parameters.ToJsonString()) is { Root.Length: > 0 } values)
        {
            options = PipeScriptRoots.FindScripts(PipeScriptRoots.Resolve(_options, values.Root), values.Path, values.IncludeSubfolders)
                .Select(s => new PipeParameterOption(s)).ToList();
        }
        return Task.FromResult(options);
    }
}

// ---------------------------------------------------------------- Run Scripts Repo

public sealed class RunScriptsRepoNodeParameters : PipeScriptCommonParameters
{
    [PipeOptions]
    [PipeParameter(Label = "Git server", Order = 1, Expressions = false, Description = "A Git server configured by the host")]
    public string? Server { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Repository", Order = 2, Expressions = false)]
    public string? Repository { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Branch", Order = 3, Expressions = false, Description = "Empty for the repository's default branch")]
    public string? Branch { get; set; }

    [PipeParameter(Label = "Path", Order = 4, Expressions = false, Placeholder = "nightly/ or nightly/backup.ps1",
        Description = "File or folder inside the repository; empty for all of it")]
    public string? Path { get; set; }

    [PipeParameter(Label = "Include subfolders", Order = 5, Expressions = false)]
    public bool IncludeSubfolders { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Scripts", Order = 6, Expressions = false,
        Description = "Run top to bottom, each in its own process; files added later are off until switched on")]
    public List<PipeSelection> Scripts { get; set; } = new();

    [PipeParameter(Label = "Stop on first failure", Order = 7, Expressions = false)]
    public bool StopOnFirstFailure { get; set; } = true;
}

[PipeNode(PipeScriptNodes.RunScriptsRepo, "Run Scripts Repo",
    Category = "Scripts",
    Description = "Clones a repository of a configured Git server for the run and runs its switched-on scripts in order, "
                  + "with the repository root as working directory (PIPE_WORK_DIRECTORY). The clone is deleted afterwards.")]
public sealed class RunScriptsRepoNode : IPipeNode<RunScriptsRepoNodeParameters>, IPipeNodeHints, IPipeParameterOptions
{
    private readonly PipeScriptsOptions _options;
    private readonly PipeGitServers _servers;

    public RunScriptsRepoNode(IOptions<PipeScriptsOptions> options, PipeGitServers servers)
    {
        _options = options.Value;
        _servers = servers;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        if (items.Count == 0)
        {
            return PipeNodeResult.FromMain([]);
        }

        // one clone per run of the node, for all items
        var first = context.GetParameters<RunScriptsRepoNodeParameters>(items[0]);
        if (string.IsNullOrWhiteSpace(first.Server) || string.IsNullOrWhiteSpace(first.Repository))
        {
            throw new PipeException("Select a Git server and a repository");
        }

        await using var checkout = await _servers.CloneAsync(first.Server, first.Repository, first.Branch, cancellationToken);
        context.Logger.LogInformation("Cloned {Repository} at {Commit}", first.Repository, checkout.Commit[..Math.Min(12, checkout.Commit.Length)]);
        var workDirectory = new Dictionary<string, string> { ["PIPE_WORK_DIRECTORY"] = checkout.Path };

        var results = new List<PipeItem>();
        var stop = false;
        for (var i = 0; i < items.Count && !stop; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<RunScriptsRepoNodeParameters>(items[i], i);
            var scripts = parameters.Scripts.Where(s => s.Enabled).Select(s => s.Value).ToList();
            if (scripts.Count == 0)
            {
                throw new PipeException("No script is switched on");
            }

            var execution = await PipeScriptExecution.CreateAsync(context, parameters, items[i], _options, workDirectory, cancellationToken);
            foreach (var script in scripts)
            {
                var path = PipeScriptPaths.Inside(checkout.Path, script, "repository");
                results.Add(await execution.RunAsync(path, script, checkout.Path, checkout.Commit, cancellationToken));
                if (!PipeScriptExecution.Succeeded(results[^1]) && parameters.StopOnFirstFailure)
                {
                    stop = true;
                    break;
                }
            }
        }
        return PipeScriptExecution.Result(results);
    }

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
    {
        var hints = new List<PipeNodeHint> { PipeScriptRoots.Warning };
        if (_servers.Names.Count == 0)
        {
            hints.Add(new(PipeNodeHintLevel.Warning, "No Git servers are configured (Pipe:Scripts:GitServers)."));
        }
        if (!_servers.GitAvailable)
        {
            hints.Add(new(PipeNodeHintLevel.Warning, $"git ('{_options.GitPath}') can't be started; install it or set Pipe:Scripts:GitPath."));
        }
        hints.Add(PipeScriptRoots.Runnable);
        return hints;
    }

    public async Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
    {
        var values = PipeJson.Deserialize<RunScriptsRepoNodeParameters>(parameters.ToJsonString()) ?? new();
        var hasServer = !string.IsNullOrWhiteSpace(values.Server);
        var hasRepository = hasServer && !string.IsNullOrWhiteSpace(values.Repository);

        switch (parameter)
        {
            case "server":
                return _servers.Names.Order().Select(n => new PipeParameterOption(n)).ToList();
            case "repository" when hasServer:
                return (await _servers.ListRepositoriesAsync(values.Server!, refresh, cancellationToken))
                    .Select(r => new PipeParameterOption(r.Name, Description: $"default branch {r.DefaultBranch}")).ToList();
            case "branch" when hasRepository:
                return (await _servers.ListBranchesAsync(values.Server!, values.Repository!, refresh, cancellationToken))
                    .Select(b => new PipeParameterOption(b)).ToList();
            case "scripts" when hasRepository:
                return FilterScripts(await _servers.ListFilesAsync(values.Server!, values.Repository!, values.Branch, refresh, cancellationToken), values)
                    .Select(s => new PipeParameterOption(s)).ToList();
            default:
                return [];
        }
    }

    /// <summary>Runnable files under the path (a file or folder) of the repository's file list.</summary>
    static private IEnumerable<string> FilterScripts(IReadOnlyList<string> files, RunScriptsRepoNodeParameters values)
    {
        var path = (values.Path ?? "").Trim().Replace('\\', '/').Trim('/');
        return files
            .Where(PipeScriptInterpreters.IsSupported)
            .Where(f =>
            {
                if (path.Length == 0)
                {
                    return values.IncludeSubfolders || !f.Contains('/');
                }
                if (f.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (!f.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return values.IncludeSubfolders || !f[(path.Length + 1)..].Contains('/');
            })
            .Order(StringComparer.OrdinalIgnoreCase);
    }
}
