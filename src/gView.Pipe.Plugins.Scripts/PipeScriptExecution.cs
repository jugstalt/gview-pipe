using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace gView.Pipe.Plugins.Scripts;

/// <summary>Settings all script nodes share (shown after their own parameters).</summary>
public abstract class PipeScriptCommonParameters
{
    [PipeParameter(Label = "Arguments", Order = 50, Placeholder = "--date {{ $json.date }}")]
    public string? Arguments { get; set; }

    [PipeParameter(Label = "Environment variables", Order = 51, Description = "Name → value; next to PIPE_ITEM, PIPE_WORKFLOW and PIPE_EXECUTION")]
    public Dictionary<string, string> Environment { get; set; } = new();

    [PipeCredential]
    [PipeParameter(Label = "Credential", Order = 52, Expressions = false, Description = "Its fields are passed as PIPE_CREDENTIAL_<FIELD>; their values are masked in output and logs")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Error pattern", Order = 53, Expressions = false, Placeholder = "^ERROR|Traceback",
        Description = "Regex; a script fails if a line of its output matches (besides a non-zero exit code)")]
    public string? ErrorPattern { get; set; }

    [PipeParameter(Label = "Ignore case", Order = 54, Expressions = false)]
    public bool IgnoreCase { get; set; } = true;

    [PipeParameter(Label = "Timeout per script (minutes)", Order = 55, Expressions = false)]
    public double TimeoutMinutes { get; set; } = 10;
}

/// <summary>
/// Runs scripts for one input item: environment (PIPE_* variables, own variables, credential fields), one process per
/// script, live output into the node log, credential values masked, failure by exit code or error pattern.
/// </summary>
internal sealed class PipeScriptExecution
{
    private readonly IPipeNodeContext _context;
    private readonly PipeScriptCommonParameters _parameters;
    private readonly PipeScriptsOptions _options;
    private readonly Dictionary<string, string> _environment;
    private readonly List<string> _secrets;
    private readonly Regex? _errorPattern;

    private PipeScriptExecution(IPipeNodeContext context, PipeScriptCommonParameters parameters, PipeScriptsOptions options, Dictionary<string, string> environment)
    {
        _context = context;
        _parameters = parameters;
        _options = options;
        _environment = environment;
        _secrets = environment.Where(e => e.Key.StartsWith("PIPE_CREDENTIAL_", StringComparison.Ordinal) && e.Value.Length >= 3).Select(e => e.Value).ToList();
        _errorPattern = string.IsNullOrWhiteSpace(parameters.ErrorPattern)
            ? null
            : new Regex(parameters.ErrorPattern, RegexOptions.Multiline | (parameters.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(2));
    }

    /// <param name="extraEnvironment">E.g. PIPE_WORK_DIRECTORY of a repository checkout.</param>
    static public async Task<PipeScriptExecution> CreateAsync(IPipeNodeContext context, PipeScriptCommonParameters parameters, PipeItem item,
        PipeScriptsOptions options, IReadOnlyDictionary<string, string>? extraEnvironment, CancellationToken cancellationToken)
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
        foreach (var (key, value) in extraEnvironment ?? new Dictionary<string, string>())
        {
            environment[key] = value;
        }
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
        return new PipeScriptExecution(context, parameters, options, environment);
    }

    /// <param name="path">Full path of the script (already checked to be inside its source).</param>
    /// <param name="name">How the script is shown: its path relative to the source.</param>
    public async Task<PipeItem> RunAsync(string path, string name, string workingDirectory, string? commit, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new PipeException($"Script '{name}' is missing");
        }

        var (fileName, arguments) = PipeScriptInterpreters.CommandFor(path, _parameters.Arguments, _options);
        var startInfo = new ProcessStartInfo(fileName, arguments) { WorkingDirectory = workingDirectory };
        foreach (var (key, value) in _environment)
        {
            startInfo.Environment[key] = value;
        }

        _context.Logger.LogInformation("Running {Script}", name);
        var timeout = TimeSpan.FromMinutes(_parameters.TimeoutMinutes > 0 ? _parameters.TimeoutMinutes : 10);
        var result = await PipeProcessRunner.RunAsync(startInfo, timeout,
            (line, isError) =>
            {
                var masked = Mask(line);
                if (isError)
                {
                    _context.Logger.LogWarning("{Script}: {Line}", name, masked);
                }
                else
                {
                    _context.Logger.LogInformation("{Script}: {Line}", name, masked);
                }
            },
            cancellationToken);

        var stdout = Mask(result.Stdout);
        var stderr = Mask(result.Stderr);
        var matchedLine = _errorPattern is null ? null : FirstMatch(stdout) ?? FirstMatch(stderr);
        var reason = result.TimedOut ? $"timed out after {timeout.TotalMinutes:0.#} min"
            : result.ExitCode != 0 ? $"exit code {result.ExitCode}"
            : matchedLine is not null ? $"output matched '{_parameters.ErrorPattern}'"
            : null;

        var json = new JsonObject
        {
            ["script"] = name,
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
            _context.Logger.LogError("{Script} failed: {Reason}", name, reason);
        }
        return new PipeItem(json);
    }

    static public bool Succeeded(PipeItem result) => result.Json["success"]?.GetValue<bool>() == true;

    /// <summary>Fails the node when a script failed; the results go on with OnError = Continue or ErrorOutput.</summary>
    static public PipeNodeResult Result(List<PipeItem> results)
        => results.FirstOrDefault(r => !Succeeded(r)) is { } failed
            ? throw new PipeNodeException($"Script failed: {failed.Json["script"]}: {failed.Json["reason"]}", results)
            : PipeNodeResult.FromMain(results);

    private string? FirstMatch(string text)
        => text.Split('\n').Select(line => line.TrimEnd('\r')).FirstOrDefault(line => _errorPattern!.IsMatch(line));

    private string Mask(string text)
        => _secrets.Aggregate(text, (current, secret) => current.Replace(secret, "***", StringComparison.Ordinal));
}

/// <summary>Script roots released by the host.</summary>
static internal class PipeScriptRoots
{
    static public string Resolve(PipeScriptsOptions options, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new PipeException("Select a script folder");
        }
        return options.ScriptRoots.TryGetValue(root.Trim(), out var path)
            ? Path.GetFullPath(path)
            : throw new PipeException(options.ScriptRoots.Count == 0
                ? "No script folders are released (Pipe:Scripts:ScriptRoots)"
                : $"Unknown script folder '{root}'; available: {string.Join(", ", options.ScriptRoots.Keys)}");
    }

    static public IReadOnlyList<PipeParameterOption> Options(PipeScriptsOptions options)
        => options.ScriptRoots.Keys.Order().Select(k => new PipeParameterOption(k)).ToList();

    /// <summary>Runnable files under <paramref name="path"/> (a file or folder), relative to the base folder.</summary>
    static public IReadOnlyList<string> FindScripts(string baseFolder, string? path, bool includeSubfolders)
    {
        var full = PipeScriptPaths.Inside(baseFolder, path, "script source");
        IEnumerable<string> files = File.Exists(full) ? [full]
            : Directory.Exists(full) ? Directory.EnumerateFiles(full, "*", includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];
        return files
            .Where(PipeScriptInterpreters.IsSupported)
            .Select(f => PipeScriptPaths.Relative(baseFolder, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static public PipeNodeHint Warning { get; } =
        new(PipeNodeHintLevel.Warning, "Runs scripts on the server with the rights of the host process. Only run scripts you trust.");

    static public PipeNodeHint Runnable => new(PipeNodeHintLevel.Info, $"Runnable on this host: {string.Join(", ", PipeScriptInterpreters.Extensions)}");
}
