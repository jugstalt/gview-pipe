using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace gView.Pipe.Plugins.Scripts;

public enum ScriptSource
{
    /// <summary>A file or folder in a script root released by the host.</summary>
    ScriptFolder,

    /// <summary>A file or folder in a Git repository configured by the host.</summary>
    GitRepository
}

public sealed class RunScriptsNodeParameters
{
    [PipeParameter(Label = "Source", Order = 1, Expressions = false)]
    public ScriptSource Source { get; set; } = ScriptSource.ScriptFolder;

    [PipeOptions]
    [PipeParameter(Label = "Script folder", Order = 2, Expressions = false, Description = "Source 'Script folder': a folder released by the host")]
    public string? Root { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Repository", Order = 3, Expressions = false, Description = "Source 'Git repository': a repository configured by the host")]
    public string? Repository { get; set; }

    [PipeParameter(Label = "Path", Order = 4, Expressions = false, Placeholder = "nightly/ or nightly/backup.ps1",
        Description = "File or folder inside the source; empty for all of it")]
    public string? Path { get; set; }

    [PipeParameter(Label = "Include subfolders", Order = 5, Expressions = false)]
    public bool IncludeSubfolders { get; set; }

    [PipeOptions(Refreshable = true)]
    [PipeParameter(Label = "Scripts", Order = 6, Expressions = false,
        Description = "Run top to bottom, each in its own process; files added later are off until switched on")]
    public List<PipeSelection> Scripts { get; set; } = new();

    [PipeParameter(Label = "Arguments", Order = 7, Placeholder = "--date {{ $json.date }}")]
    public string? Arguments { get; set; }

    [PipeParameter(Label = "Environment variables", Order = 8, Description = "Name → value; next to PIPE_ITEM, PIPE_WORKFLOW and PIPE_EXECUTION")]
    public Dictionary<string, string> Environment { get; set; } = new();

    [PipeCredential]
    [PipeParameter(Label = "Credential", Order = 9, Expressions = false, Description = "Its fields are passed as PIPE_CREDENTIAL_<FIELD>; their values are masked in output and logs")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Error pattern", Order = 10, Expressions = false, Placeholder = "^ERROR|Traceback",
        Description = "Regex; a script fails if a line of its output matches (besides a non-zero exit code)")]
    public string? ErrorPattern { get; set; }

    [PipeParameter(Label = "Ignore case", Order = 11, Expressions = false)]
    public bool IgnoreCase { get; set; } = true;

    [PipeParameter(Label = "Stop on first failure", Order = 12, Expressions = false)]
    public bool StopOnFirstFailure { get; set; } = true;

    [PipeParameter(Label = "Timeout per script (minutes)", Order = 13, Expressions = false)]
    public double TimeoutMinutes { get; set; } = 10;

    [PipeParameter(Label = "Use last checkout if the pull fails", Order = 14, Expressions = false)]
    public bool UseLastCheckoutIfPullFails { get; set; }
}

[PipeNode("2f3f6f81-f55e-4b5c-87c1-59632ef03445", "Run Scripts",
    Category = "Scripts",
    Description = "Runs .bat/.cmd, PowerShell, Python or shell scripts from a released folder or a Git repository, "
                  + "once per input item. One output item per script with exit code and output.")]
public sealed class RunScriptsNode : IPipeNode<RunScriptsNodeParameters>, IPipeNodeHints, IPipeParameterOptions
{
    private readonly PipeScriptsOptions _options;
    private readonly PipeScriptRepositories _repositories;

    public RunScriptsNode(IOptions<PipeScriptsOptions> options, PipeScriptRepositories repositories)
    {
        _options = options.Value;
        _repositories = repositories;
    }

    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var results = new List<PipeItem>();
        PipeScriptCheckout? checkout = null;
        string? failure = null;

        for (var i = 0; i < items.Count && failure is null; i++)
        {
            var parameters = context.GetParameters<RunScriptsNodeParameters>(items[i], i);

            // a repository is updated once per run, not per item
            if (parameters.Source == ScriptSource.GitRepository && checkout is null)
            {
                checkout = await _repositories.UpdateAsync(Required(parameters.Repository, "Select a repository"), parameters.UseLastCheckoutIfPullFails, cancellationToken);
                context.Logger.LogInformation("Repository {Repository} at {Commit}", parameters.Repository, checkout.Commit[..Math.Min(12, checkout.Commit.Length)]);
                if (checkout.Warning is { } warning)
                {
                    context.Logger.LogWarning("{Warning}", warning);
                }
            }

            var baseFolder = parameters.Source == ScriptSource.GitRepository ? checkout!.Path : RootPath(parameters.Root);
            var scripts = parameters.Scripts.Where(s => s.Enabled).Select(s => s.Value).ToList();
            if (scripts.Count == 0)
            {
                throw new PipeException("No script is switched on");
            }

            var environment = await EnvironmentAsync(context, parameters, items[i], cancellationToken);
            var secrets = environment.Where(e => e.Key.StartsWith("PIPE_CREDENTIAL_", StringComparison.Ordinal) && e.Value.Length >= 3)
                .Select(e => e.Value).ToList();
            var errorPattern = string.IsNullOrWhiteSpace(parameters.ErrorPattern)
                ? null
                : new Regex(parameters.ErrorPattern, RegexOptions.Multiline | (parameters.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(2));

            foreach (var script in scripts)
            {
                var result = await RunAsync(context, parameters, baseFolder, script, environment, secrets, errorPattern, checkout?.Commit, cancellationToken);
                results.Add(result);

                if (result.Json["success"]!.GetValue<bool>() == false)
                {
                    failure ??= $"{script}: {result.Json["reason"]}";
                    if (parameters.StopOnFirstFailure)
                    {
                        break;
                    }
                }
            }
        }

        if (failure is not null)
        {
            // the results go on with OnError = Continue or ErrorOutput
            throw new PipeNodeException($"Script failed: {failure}", results);
        }
        return PipeNodeResult.FromMain(results);
    }

    private async Task<PipeItem> RunAsync(
        IPipeNodeContext context,
        RunScriptsNodeParameters parameters,
        string baseFolder,
        string script,
        Dictionary<string, string> environment,
        List<string> secrets,
        Regex? errorPattern,
        string? commit,
        CancellationToken cancellationToken)
    {
        var path = PipeScriptPaths.Inside(baseFolder, script, "script source");
        if (!File.Exists(path))
        {
            throw new PipeException($"Script '{script}' is missing");
        }

        var (fileName, arguments) = PipeScriptInterpreters.CommandFor(path, parameters.Arguments, _options);
        var startInfo = new ProcessStartInfo(fileName, arguments) { WorkingDirectory = Path.GetDirectoryName(path)! };
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        context.Logger.LogInformation("Running {Script}", script);
        var timeout = TimeSpan.FromMinutes(parameters.TimeoutMinutes > 0 ? parameters.TimeoutMinutes : 10);
        var result = await PipeProcessRunner.RunAsync(startInfo, timeout,
            (line, isError) =>
            {
                var masked = Mask(line, secrets);
                if (isError)
                {
                    context.Logger.LogWarning("{Script}: {Line}", script, masked);
                }
                else
                {
                    context.Logger.LogInformation("{Script}: {Line}", script, masked);
                }
            },
            cancellationToken);

        var stdout = Mask(result.Stdout, secrets);
        var stderr = Mask(result.Stderr, secrets);
        var matchedLine = errorPattern is null ? null : FirstMatch(errorPattern, stdout) ?? FirstMatch(errorPattern, stderr);
        var reason = result.TimedOut ? $"timed out after {timeout.TotalMinutes:0.#} min"
            : result.ExitCode != 0 ? $"exit code {result.ExitCode}"
            : matchedLine is not null ? $"output matched '{parameters.ErrorPattern}'"
            : null;

        var json = new JsonObject
        {
            ["script"] = script,
            ["success"] = reason is null,
            ["exitCode"] = result.ExitCode,
            ["reason"] = reason,
            ["matchedLine"] = matchedLine,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["durationMs"] = (long)result.Duration.TotalMilliseconds
        };
        if (result.Truncated)
        {
            json["truncated"] = true;
        }
        if (commit is not null)
        {
            json["commit"] = commit;
        }

        if (reason is not null)
        {
            context.Logger.LogError("{Script} failed: {Reason}", script, reason);
        }
        return new PipeItem(json);
    }

    static private string? FirstMatch(Regex pattern, string text)
        => text.Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => pattern.IsMatch(line));

    static private string Mask(string text, List<string> secrets)
        => secrets.Aggregate(text, (current, secret) => current.Replace(secret, "***", StringComparison.Ordinal));

    private async Task<Dictionary<string, string>> EnvironmentAsync(IPipeNodeContext context, RunScriptsNodeParameters parameters, PipeItem item, CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PIPE_ITEM"] = item.Json.ToJsonString(PipeJson.Options),
            ["PIPE_WORKFLOW"] = context.WorkflowName,
            ["PIPE_EXECUTION"] = context.ExecutionId,
            // Python prints UTF-8, like the output is read
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUTF8"] = "1"
        };
        foreach (var (key, value) in parameters.Environment.Where(e => e.Key.Trim().Length > 0))
        {
            environment[key.Trim()] = value;
        }

        if (!string.IsNullOrEmpty(parameters.CredentialId))
        {
            var credential = await context.GetCredentialAsync(parameters.CredentialId, cancellationToken);
            foreach (var (field, value) in credential.Data)
            {
                if (value is JsonValue v)
                {
                    environment[$"PIPE_CREDENTIAL_{field.ToUpperInvariant()}"] = v.ToString();
                }
            }
        }
        return environment;
    }

    private string RootPath(string? root)
    {
        var name = Required(root, "Select a script folder");
        return _options.ScriptRoots.TryGetValue(name.Trim(), out var path)
            ? Path.GetFullPath(path)
            : throw new PipeException(_options.ScriptRoots.Count == 0
                ? "No script folders are released (Pipe:Scripts:ScriptRoots)"
                : $"Unknown script folder '{name}'; available: {string.Join(", ", _options.ScriptRoots.Keys)}");
    }

    static private string Required(string? value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new PipeException(message) : value;

    // ---- editor ----

    public IReadOnlyList<PipeNodeHint> GetHints(JsonObject parameters)
    {
        var hints = new List<PipeNodeHint>
        {
            new(PipeNodeHintLevel.Warning, "Runs scripts on the server with the rights of the host process. Only switch on scripts you trust.")
        };

        var source = parameters["source"]?.GetValue<string>();
        if (source is "gitRepository")
        {
            if (_options.Repositories.Count == 0)
            {
                hints.Add(new(PipeNodeHintLevel.Warning, "No repositories are configured (Pipe:Scripts:Repositories)."));
            }
            if (!_repositories.GitAvailable)
            {
                hints.Add(new(PipeNodeHintLevel.Warning, $"git ('{_options.GitPath}') can't be started; install it or set Pipe:Scripts:GitPath."));
            }
        }
        else if (_options.ScriptRoots.Count == 0)
        {
            hints.Add(new(PipeNodeHintLevel.Warning, "No script folders are released (Pipe:Scripts:ScriptRoots)."));
        }

        hints.Add(new(PipeNodeHintLevel.Info, $"Runnable on this host: {string.Join(", ", PipeScriptInterpreters.Extensions)}"));
        return hints;
    }

    public async Task<IReadOnlyList<PipeParameterOption>> GetOptionsAsync(string parameter, JsonObject parameters, bool refresh, CancellationToken cancellationToken)
    {
        switch (parameter)
        {
            case "root":
                return _options.ScriptRoots.Keys.Order().Select(k => new PipeParameterOption(k)).ToList();
            case "repository":
                return _options.Repositories.Select(r => new PipeParameterOption(r.Key, Description: $"{r.Value.Url} ({r.Value.Branch})")).OrderBy(o => o.Value).ToList();
            case "scripts":
                return await FindScriptsAsync(PipeJson.Deserialize<RunScriptsNodeParameters>(parameters.ToJsonString()) ?? new(), refresh, cancellationToken);
            default:
                return [];
        }
    }

    /// <summary>Runnable files under the path, relative to the source (forward slashes).</summary>
    private async Task<IReadOnlyList<PipeParameterOption>> FindScriptsAsync(RunScriptsNodeParameters parameters, bool refresh, CancellationToken cancellationToken)
    {
        string baseFolder;
        if (parameters.Source == ScriptSource.GitRepository)
        {
            if (string.IsNullOrWhiteSpace(parameters.Repository))
            {
                return [];
            }
            baseFolder = (refresh
                ? await _repositories.UpdateAsync(parameters.Repository, useLastCheckoutIfFailing: false, cancellationToken)
                : await _repositories.GetAsync(parameters.Repository, cancellationToken)).Path;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(parameters.Root))
            {
                return [];
            }
            baseFolder = RootPath(parameters.Root);
        }

        var path = PipeScriptPaths.Inside(baseFolder, parameters.Path, "script source");
        IEnumerable<string> files = File.Exists(path) ? [path]
            : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", parameters.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];

        return files
            .Where(PipeScriptInterpreters.IsSupported)
            // nothing from the repository's own folder
            .Where(f => !f.Contains($"{System.IO.Path.DirectorySeparatorChar}.git{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => PipeScriptPaths.Relative(baseFolder, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => new PipeParameterOption(f))
            .ToList();
    }
}
