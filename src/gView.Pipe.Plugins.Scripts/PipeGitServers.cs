using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace gView.Pipe.Plugins.Scripts;

public sealed record PipeGitRepository(string Name, string DefaultBranch, string CloneUrl);

/// <summary>A clone for one run; disposing deletes it.</summary>
public sealed class PipeGitCheckout : IAsyncDisposable
{
    internal PipeGitCheckout(string path, string commit)
    {
        Path = path;
        Commit = commit;
    }

    public string Path { get; }
    public string Commit { get; }

    public ValueTask DisposeAsync()
    {
        PipeGitServers.DeleteFolder(Path);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The configured Git servers: their repositories, branches and files through the server's API (Gitea, GitHub),
/// and a fresh shallow clone per run through the installed git command line, deleted afterwards.
/// Credentials reach the API as an Authorization header and git as an HTTP header in environment variables,
/// never the URL, the folder or a log.
/// </summary>
public sealed class PipeGitServers
{
    public const string HttpClientName = "gview-pipe-git";

    static private readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(10);
    static private readonly TimeSpan CacheTime = TimeSpan.FromSeconds(30);
    static private int _leftoversRemoved;

    private readonly PipeScriptsOptions _options;
    private readonly IPipeCredentialStore _credentials;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PipeGitServers> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, object Value)> _cache = new();

    public PipeGitServers(IOptions<PipeScriptsOptions> options, IPipeCredentialStore credentials, IHttpClientFactory http, ILogger<PipeGitServers> logger)
    {
        _options = options.Value;
        _credentials = credentials;
        _http = http;
        _logger = logger;
        RemoveLeftovers();
    }

    public IReadOnlyCollection<string> Names => _options.GitServers.Keys;

    /// <summary>Whether the git command line can be started (cached; for editor hints).</summary>
    public bool GitAvailable => _gitAvailable ??= CheckGit();
    private bool? _gitAvailable;

    // ---- API ----

    /// <summary>Repositories of the server's owner (organization or user), filtered by the server's RepositoryFilter.</summary>
    public async Task<IReadOnlyList<PipeGitRepository>> ListRepositoriesAsync(string serverName, bool refresh, CancellationToken cancellationToken)
    {
        var server = Server(serverName);
        var repositories = await CachedAsync($"repos|{serverName}", refresh, async () =>
        {
            var owner = Uri.EscapeDataString(server.Owner);
            var perPage = server.Provider == PipeGitProviders.GitHub ? "per_page" : "limit";
            // an organization, or else a user
            var list = await GetPagedAsync(server, $"orgs/{owner}/repos?{perPage}=100", allowNotFound: true, cancellationToken)
                       ?? await GetPagedAsync(server, $"users/{owner}/repos?{perPage}=100", allowNotFound: false, cancellationToken)
                       ?? [];
            return list.OfType<JsonObject>()
                .Select(r => new PipeGitRepository(Text(r["name"]), Text(r["default_branch"]), Text(r["clone_url"])))
                .Where(r => r.Name.Length > 0)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });

        if (string.IsNullOrWhiteSpace(server.RepositoryFilter))
        {
            return repositories;
        }
        var filter = new Regex(server.RepositoryFilter, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return repositories.Where(r => filter.IsMatch(r.Name)).ToList();
    }

    public async Task<PipeGitRepository> RepositoryAsync(string serverName, string repositoryName, CancellationToken cancellationToken)
        => (await ListRepositoriesAsync(serverName, refresh: false, cancellationToken)).FirstOrDefault(r => r.Name.Equals(repositoryName, StringComparison.OrdinalIgnoreCase))
           ?? (await ListRepositoriesAsync(serverName, refresh: true, cancellationToken)).FirstOrDefault(r => r.Name.Equals(repositoryName, StringComparison.OrdinalIgnoreCase))
           ?? throw new PipeException($"Repository '{repositoryName}' is not offered by '{serverName}' (missing, no access, or filtered out)");

    public async Task<IReadOnlyList<string>> ListBranchesAsync(string serverName, string repositoryName, bool refresh, CancellationToken cancellationToken)
    {
        var server = Server(serverName);
        var repository = await RepositoryAsync(serverName, repositoryName, cancellationToken);
        return await CachedAsync($"branches|{serverName}|{repository.Name}", refresh, async () =>
        {
            var perPage = server.Provider == PipeGitProviders.GitHub ? "per_page" : "limit";
            var list = await GetPagedAsync(server, $"repos/{Uri.EscapeDataString(server.Owner)}/{Uri.EscapeDataString(repository.Name)}/branches?{perPage}=100", false, cancellationToken) ?? [];
            return (IReadOnlyList<string>)list.OfType<JsonObject>().Select(b => Text(b["name"])).Where(n => n.Length > 0).Order(StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    /// <summary>All file paths of the branch (forward slashes), from the server's tree API; no clone.</summary>
    public async Task<IReadOnlyList<string>> ListFilesAsync(string serverName, string repositoryName, string? branch, bool refresh, CancellationToken cancellationToken)
    {
        var server = Server(serverName);
        var repository = await RepositoryAsync(serverName, repositoryName, cancellationToken);
        var reference = string.IsNullOrWhiteSpace(branch) ? repository.DefaultBranch : branch.Trim();
        return await CachedAsync($"files|{serverName}|{repository.Name}|{reference}", refresh, async () =>
        {
            var path = $"repos/{Uri.EscapeDataString(server.Owner)}/{Uri.EscapeDataString(repository.Name)}/git/trees/{Uri.EscapeDataString(reference)}"
                       + (server.Provider == PipeGitProviders.GitHub ? "?recursive=1" : "?recursive=true&per_page=10000");
            var tree = await GetAsync(server, path, allowNotFound: false, cancellationToken);
            return (IReadOnlyList<string>)(tree?["tree"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(e => Text(e["type"]) == "blob")
                .Select(e => Text(e["path"]))
                .ToList();
        });
    }

    // ---- clone ----

    /// <summary>A shallow clone of the branch into a new folder under RepositoriesPath; dispose it to delete it.</summary>
    public async Task<PipeGitCheckout> CloneAsync(string serverName, string repositoryName, string? branch, CancellationToken cancellationToken)
    {
        var server = Server(serverName);
        var repository = await RepositoryAsync(serverName, repositoryName, cancellationToken);
        var reference = string.IsNullOrWhiteSpace(branch) ? repository.DefaultBranch : branch.Trim();
        var folder = Path.Combine(Path.GetFullPath(_options.RepositoriesPath), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var auth = await AuthAsync(server, cancellationToken);
            var environment = auth is null ? null : new Dictionary<string, string>
            {
                ["GIT_CONFIG_COUNT"] = "1",
                ["GIT_CONFIG_KEY_0"] = "http.extraHeader",
                ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {auth.Value.Basic}"
            };
            await GitAsync(folder, environment, cancellationToken, "clone", "--depth", "1", "--branch", reference, "--single-branch", "--", repository.CloneUrl, ".");
            var commit = (await GitAsync(folder, null, cancellationToken, "rev-parse", "HEAD")).Trim();
            return new PipeGitCheckout(folder, commit);
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
    }

    /// <summary>Deletes a folder, also git's read-only object files.</summary>
    static internal void DeleteFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a process may still hold a file; the next start removes the rest
        }
    }

    /// <summary>Clones of runs a crash interrupted; once per process, before any clone of it exists.</summary>
    private void RemoveLeftovers()
    {
        if (Interlocked.Exchange(ref _leftoversRemoved, 1) == 1)
        {
            return;
        }
        var root = Path.GetFullPath(_options.RepositoriesPath);
        if (!Directory.Exists(root))
        {
            return;
        }
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            _logger.LogInformation("Removing leftover script checkout {Folder}", folder);
            DeleteFolder(folder);
        }
    }

    // ---- helpers ----

    private PipeGitServer Server(string name)
        => _options.GitServers.TryGetValue(name.Trim(), out var server)
            ? server
            : throw new PipeException(_options.GitServers.Count == 0
                ? "No Git servers are configured (Pipe:Scripts:GitServers)"
                : $"Unknown Git server '{name}'; available: {string.Join(", ", _options.GitServers.Keys)}");

    private async Task<T> CachedAsync<T>(string key, bool refresh, Func<Task<T>> load) where T : class
    {
        if (!refresh && _cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < CacheTime)
        {
            return (T)cached.Value;
        }
        var value = await load();
        _cache[key] = (DateTimeOffset.UtcNow, value);
        return value;
    }

    static private Uri ApiBase(PipeGitServer server)
    {
        var url = server.Url.TrimEnd('/');
        if (server.Provider == PipeGitProviders.GitHub)
        {
            // github.com has its own API host; GitHub Enterprise serves it under /api/v3
            return new Uri(new Uri(url).Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com/" : $"{url}/api/v3/");
        }
        return new Uri($"{url}/api/v1/");
    }

    /// <summary>All pages of a list (until a page comes back short); null for 404 when allowed.</summary>
    private async Task<JsonArray?> GetPagedAsync(PipeGitServer server, string path, bool allowNotFound, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var all = new JsonArray();
        for (var page = 1; page <= 50; page++)
        {
            var result = await GetAsync(server, $"{path}&page={page}", allowNotFound && page == 1, cancellationToken);
            if (result is null)
            {
                return null;
            }

            var items = (result as JsonArray ?? []).ToList();
            foreach (var item in items)
            {
                all.Add(item?.DeepClone());
            }
            // a short page is the last one
            if (items.Count < pageSize)
            {
                break;
            }
        }
        return all;
    }

    private async Task<JsonNode?> GetAsync(PipeGitServer server, string path, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase(server), path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("gView.Pipe", "1.0"));
        if (await AuthAsync(server, cancellationToken) is { } auth)
        {
            request.Headers.Authorization = auth.Token is { } token
                ? new AuthenticationHeaderValue("Bearer", token)
                : new AuthenticationHeaderValue("Basic", auth.Basic);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new PipeException($"Git server '{server.Url}' can't be reached: {ex.Message}", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new PipeException($"Git server '{server.Url}' answered {(int)response.StatusCode} {response.ReasonPhrase} for {path.Split('?')[0]}");
            }
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    /// <summary>Basic: user + password (or token); Bearer: the token, as password of "x-access-token" for git.</summary>
    private async Task<(string Basic, string? Token)?> AuthAsync(PipeGitServer server, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(server.CredentialId))
        {
            return null;
        }
        var credential = await _credentials.GetAsync(server.CredentialId, cancellationToken)
            ?? throw new PipeException($"Credential '{server.CredentialId}' of the Git server not found");
        var data = credential.Data;
        if (data["token"] is JsonValue token)
        {
            var value = token.GetValue<string>();
            return (Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{value}")), value);
        }
        return (Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Text(data["userName"])}:{Text(data["password"])}")), null);
    }

    static private string Text(JsonNode? node) => node is JsonValue v ? v.ToString() : "";

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
            throw new PipeException($"git {arguments[0]} failed: {(result.TimedOut ? "timed out" : result.Stderr.Trim())}");
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
}
