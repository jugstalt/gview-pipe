using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Scripts.Tests;

/// <summary>
/// Run Scripts Repo against a fake Gitea API (repositories, branches, files) whose clone URL is a local repository,
/// and, with PIPE_TEST_DEV_GITEA=1, against the real dev Gitea on localhost:3000.
/// </summary>
public sealed class RunScriptsRepoNodeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gview-pipe-repo-").FullName;
    private readonly FakeGitea _api = new();

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string ReposPath => Path.Combine(_root, "repos");

    private ServiceProvider Create(string url = "http://gitea.test", bool fakeApi = true)
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
            options.PluginAssemblies.Add(typeof(RunScriptsRepoNode).Assembly);
        });
        services.Configure<PipeScriptsOptions>(o =>
        {
            o.RepositoriesPath = ReposPath;
            o.GitServers["gitea"] = new PipeGitServer { Provider = PipeGitProviders.Gitea, Url = url, Owner = "dev", CredentialId = "gitea" };
        });
        if (fakeApi)
        {
            services.AddHttpClient(PipeGitServers.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _api);
        }

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "gitea",
            Name = "Gitea",
            TypeId = Guid.Parse(PipeBuiltInCredentials.BasicAuth),
            Data = new JsonObject { ["userName"] = "gitea", ["password"] = "gitea" }
        }).GetAwaiter().GetResult();
        return provider;
    }

    /// <summary>A local repository with a script at the root and one in a subfolder; the fake API's clone URL.</summary>
    private string CreateOrigin()
    {
        var origin = Path.Combine(_root, "origin");
        Git(_root, "init", "-b", "main", origin);
        Git(origin, "config", "user.email", "test@example.com");
        Git(origin, "config", "user.name", "Test");
        Directory.CreateDirectory(Path.Combine(origin, "nightly"));
        File.WriteAllText(Path.Combine(origin, "where.bat"), "@echo off\r\necho %CD%^|%PIPE_WORK_DIRECTORY%\r\n");
        File.WriteAllText(Path.Combine(origin, "nightly", "report.bat"), "@echo off\r\necho report\r\n");
        File.WriteAllText(Path.Combine(origin, "README.md"), "scripts");
        Git(origin, "add", ".");
        Git(origin, "commit", "-m", "scripts");
        _api.CloneUrl = origin;
        return origin;
    }

    static private object Scripts(params string[] names) => names.Select(n => new { value = n, enabled = true }).ToArray();

    static private async Task<PipeExecution> RunAsync(ServiceProvider services, object parameters)
    {
        var workflow = new PipeWorkflowBuilder("Repo")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("run", PipeScriptNodes.RunScriptsRepo, parameters)
            .Connect("start", "run")
            .Build();
        return await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow);
    }

    static private List<JsonObject> Results(PipeExecution execution)
        => execution.NodeRuns["run"].Outputs!["main"].Select(i => i.Json).ToList();

    [Fact]
    public async Task Editor_options_come_from_the_api_without_a_clone()
    {
        using var services = Create();
        var registry = services.GetRequiredService<IPipeNodeRegistry>();
        var descriptor = registry.Find(Guid.Parse(PipeScriptNodes.RunScriptsRepo))!;
        async Task<string[]> Options(string parameter, JsonObject parameters)
            => (await registry.GetParameterOptionsAsync(descriptor, parameter, parameters)).Select(o => o.Value).ToArray();

        var repo = new JsonObject { ["server"] = "gitea", ["repository"] = "scripts" };

        Assert.Equal(["gitea"], await Options("server", new JsonObject()));
        Assert.Equal(["scripts"], await Options("repository", new JsonObject { ["server"] = "gitea" }));
        Assert.Equal(["main", "release"], await Options("branch", repo));
        Assert.Equal(["where.bat"], await Options("scripts", repo));
        Assert.Equal(["nightly/report.bat", "where.bat"], await Options("scripts", new JsonObject { ["server"] = "gitea", ["repository"] = "scripts", ["includeSubfolders"] = true }));
        Assert.Equal(["nightly/report.bat"], await Options("scripts", new JsonObject { ["server"] = "gitea", ["repository"] = "scripts", ["path"] = "nightly" }));
        Assert.Equal("Basic Z2l0ZWE6Z2l0ZWE=", _api.LastAuthorization);
        Assert.False(Directory.Exists(ReposPath) && Directory.EnumerateDirectories(ReposPath).Any());
    }

    [Fact]
    public async Task Run_clones_into_a_temporary_folder_runs_at_the_root_and_deletes_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && CanRun("git"), "needs git (and cmd for the script)");
        var origin = CreateOrigin();
        using var services = Create();

        var execution = await RunAsync(services, new { server = "gitea", repository = "scripts", scripts = Scripts("where.bat", "nightly/report.bat") });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var results = Results(execution);
        var (workingDirectory, workDirectoryVariable) = results[0]["stdout"]!.GetValue<string>().Trim().Split('|') switch { var p => (p[0], p[1]) };
        Assert.Equal(workDirectoryVariable, workingDirectory, ignoreCase: true);
        Assert.StartsWith(ReposPath, workingDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("report", results[1]["stdout"]!.GetValue<string>().Trim());
        Assert.Equal(Git(origin, "rev-parse", "HEAD").Trim(), results[0]["commit"]!.GetValue<string>());
        // the clone is gone
        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task Unknown_repository_fails_clearly()
    {
        using var services = Create();

        var execution = await RunAsync(services, new { server = "gitea", repository = "nope", scripts = Scripts("x.bat") });

        Assert.Contains("Repository 'nope' is not offered by 'gitea'", execution.NodeRuns["run"].Error);
    }

    [Fact]
    public async Task Local_gitea_dev_scripts_can_be_listed_cloned_and_run()
    {
        // opt-in: the dev repository is edited by hand, so normal test runs (and the deploy build) never depend on it
        Assert.SkipUnless(Environment.GetEnvironmentVariable("PIPE_TEST_DEV_GITEA") == "1", "set PIPE_TEST_DEV_GITEA=1 to test against the dev Gitea");
        Assert.SkipUnless(OperatingSystem.IsWindows() && CanRun("git") && GiteaReachable(), "needs the dev Gitea on localhost:3000 with dev/scripts");
        using var services = Create("http://localhost:3000", fakeApi: false);
        var registry = services.GetRequiredService<IPipeNodeRegistry>();
        var descriptor = registry.Find(Guid.Parse(PipeScriptNodes.RunScriptsRepo))!;

        var repositories = await registry.GetParameterOptionsAsync(descriptor, "repository", new JsonObject { ["server"] = "gitea" });
        Assert.Contains(repositories, r => r.Value == "scripts");
        var scripts = await registry.GetParameterOptionsAsync(descriptor, "scripts", new JsonObject { ["server"] = "gitea", ["repository"] = "scripts" });
        Assert.SkipUnless(scripts.Any(s => s.Value == "hello.bat"), "dev/scripts has no hello.bat");

        var execution = await RunAsync(services, new { server = "gitea", repository = "scripts", scripts = Scripts("hello.bat") });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Contains("Hello", Results(execution)[0]["stdout"]!.GetValue<string>());
    }

    static private bool GiteaReachable()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            return client.GetAsync("http://localhost:3000/api/v1/version").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static private bool CanRun(string program)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            process!.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static private string Git(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }

    /// <summary>Answers the few Gitea API calls the node makes.</summary>
    private sealed class FakeGitea : HttpMessageHandler
    {
        public string CloneUrl { get; set; } = "http://gitea.test/dev/scripts.git";
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            var path = request.RequestUri!.AbsolutePath;
            JsonNode? body = path switch
            {
                "/api/v1/orgs/dev/repos" => new JsonArray(new JsonObject { ["name"] = "scripts", ["default_branch"] = "main", ["clone_url"] = CloneUrl }),
                "/api/v1/repos/dev/scripts/branches" => new JsonArray(new JsonObject { ["name"] = "release" }, new JsonObject { ["name"] = "main" }),
                "/api/v1/repos/dev/scripts/git/trees/main" => new JsonObject
                {
                    ["tree"] = new JsonArray(
                        new JsonObject { ["path"] = "README.md", ["type"] = "blob" },
                        new JsonObject { ["path"] = "nightly", ["type"] = "tree" },
                        new JsonObject { ["path"] = "nightly/report.bat", ["type"] = "blob" },
                        new JsonObject { ["path"] = "where.bat", ["type"] = "blob" })
                },
                _ => null
            };

            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
}
