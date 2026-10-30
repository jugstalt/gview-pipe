using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Scripts;

public sealed record PipeScriptCheckout(string Path, string Commit, string? Warning);

/// <summary>
/// Checkouts of the configured repositories, through the installed git command line. Each repository has one
/// working folder; updating resets it to the remote branch, so exactly what is in the repository runs.
/// Credentials go to git as an HTTP header in environment variables, never into the URL, the folder or a log.
/// </summary>
public sealed class PipeScriptRepositories
{
    static private readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    static private readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(10);

    private readonly PipeScriptsOptions _options;
    private readonly IPipeCredentialStore _credentials;
    private readonly ILogger<PipeScriptRepositories> _logger;

    public PipeScriptRepositories(IOptions<PipeScriptsOptions> options, IPipeCredentialStore credentials, ILogger<PipeScriptRepositories> logger)
    {
        _options = options.Value;
        _credentials = credentials;
        _logger = logger;
    }

    /// <summary>Whether the git command line can be started (cached; for editor hints).</summary>
    public bool GitAvailable => _gitAvailable ??= CheckGit();
    private bool? _gitAvailable;

    /// <summary>The repository's working folder, cloned if it doesn't exist yet; no update otherwise.</summary>
    internal async Task<PipeScriptCheckout> GetAsync(string name, CancellationToken cancellationToken)
    {
        var (repository, folder) = Resolve(name);
        await using var _ = await LockAsync(name, cancellationToken);

        if (!IsCheckout(folder))
        {
            await CloneAsync(repository, folder, cancellationToken);
        }
        return new PipeScriptCheckout(folder, await CommitAsync(folder, cancellationToken), null);
    }

    /// <summary>Clones or updates to the latest commit of the configured branch.</summary>
    /// <param name="useLastCheckoutIfFailing">Run with the existing checkout when the update fails (e.g. offline).</param>
    internal async Task<PipeScriptCheckout> UpdateAsync(string name, bool useLastCheckoutIfFailing, CancellationToken cancellationToken)
    {
        var (repository, folder) = Resolve(name);
        await using var _ = await LockAsync(name, cancellationToken);

        try
        {
            if (IsCheckout(folder) && await RemoteMatchesAsync(repository, folder, cancellationToken))
            {
                var auth = await AuthEnvironmentAsync(repository, cancellationToken);
                await GitAsync(folder, auth, cancellationToken, "fetch", "--prune", "origin", repository.Branch);
                await GitAsync(folder, null, cancellationToken, "reset", "--hard", $"origin/{repository.Branch}");
                await GitAsync(folder, null, cancellationToken, "clean", "-fdx");
            }
            else
            {
                await CloneAsync(repository, folder, cancellationToken);
            }
            return new PipeScriptCheckout(folder, await CommitAsync(folder, cancellationToken), null);
        }
        catch (PipeException ex) when (useLastCheckoutIfFailing && IsCheckout(folder))
        {
            _logger.LogWarning("Updating repository {Repository} failed, using the last checkout: {Error}", name, ex.Message);
            return new PipeScriptCheckout(folder, await CommitAsync(folder, cancellationToken), $"Update failed, ran the last checkout: {ex.Message}");
        }
    }

    private (PipeScriptRepository Repository, string Folder) Resolve(string name)
    {
        if (!_options.Repositories.TryGetValue(name.Trim(), out var repository))
        {
            throw new PipeException(_options.Repositories.Count == 0
                ? "No script repositories are configured (Pipe:Scripts:Repositories)"
                : $"Unknown repository '{name}'; available: {string.Join(", ", _options.Repositories.Keys)}");
        }
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
        {
            throw new PipeException($"Repository name '{name}' must only use letters, digits, '-', '_' and '.'");
        }
        return (repository, Path.Combine(Path.GetFullPath(_options.RepositoriesPath), name.Trim()));
    }

    static private async Task<IAsyncDisposable> LockAsync(string name, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Release(semaphore);
    }

    static private bool IsCheckout(string folder) => Directory.Exists(Path.Combine(folder, ".git"));

    private async Task CloneAsync(PipeScriptRepository repository, string folder, CancellationToken cancellationToken)
    {
        // a folder that is no checkout (or of another URL) is replaced; it lies inside RepositoriesPath
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);

        var auth = await AuthEnvironmentAsync(repository, cancellationToken);
        await GitAsync(Path.GetDirectoryName(folder)!, auth, cancellationToken,
            "clone", "--branch", repository.Branch, "--single-branch", "--", repository.Url, folder);
    }

    private async Task<bool> RemoteMatchesAsync(PipeScriptRepository repository, string folder, CancellationToken cancellationToken)
        => (await GitAsync(folder, null, cancellationToken, "remote", "get-url", "origin")).Trim() == repository.Url.Trim();

    private async Task<string> CommitAsync(string folder, CancellationToken cancellationToken)
        => (await GitAsync(folder, null, cancellationToken, "rev-parse", "HEAD")).Trim();

    /// <summary>Basic Auth (user + password or token) or Bearer Token (as password of "x-access-token").</summary>
    private async Task<Dictionary<string, string>?> AuthEnvironmentAsync(PipeScriptRepository repository, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(repository.CredentialId))
        {
            return null;
        }

        var credential = await _credentials.GetAsync(repository.CredentialId, cancellationToken)
            ?? throw new PipeException($"Credential '{repository.CredentialId}' of the repository not found");
        var data = credential.Data;
        var (user, password) = data["token"] is JsonValue token
            ? ("x-access-token", token.GetValue<string>())
            : (Text(data["userName"]), Text(data["password"]));
        var header = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.extraHeader",
            ["GIT_CONFIG_VALUE_0"] = header
        };

        static string Text(JsonNode? node) => node is JsonValue v ? v.GetValue<string>() : "";
    }

    private async Task<string> GitAsync(string workingDirectory, Dictionary<string, string>? environment, CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_options.GitPath) { WorkingDirectory = workingDirectory };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        // never ask on a console nobody sees
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        foreach (var (key, value) in environment ?? [])
        {
            startInfo.Environment[key] = value;
        }

        var result = await PipeProcessRunner.RunAsync(startInfo, GitTimeout, null, cancellationToken);
        if (result.TimedOut || result.ExitCode != 0)
        {
            var message = result.TimedOut ? "timed out" : result.Stderr.Trim();
            throw new PipeException($"git {arguments[0]} failed: {message}");
        }
        return result.Stdout;
    }

    private bool CheckGit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(_options.GitPath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class Release(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
