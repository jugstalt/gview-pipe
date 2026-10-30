using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Scripts.Tests;

/// <summary>Run Scripts with real interpreters; tests needing one that isn't installed are skipped.</summary>
public sealed class RunScriptsNodeTests : IDisposable
{
    private const string RunScripts = "2f3f6f81-f55e-4b5c-87c1-59632ef03445";

    private readonly string _root = Directory.CreateTempSubdirectory("gview-pipe-scripts-").FullName;
    private readonly ServiceProvider _services;

    public RunScriptsNodeTests()
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
            options.PluginAssemblies.Add(typeof(RunScriptsNode).Assembly);
        });
        services.Configure<PipeScriptsOptions>(o =>
        {
            o.ScriptRoots["jobs"] = Path.Combine(_root, "jobs");
            o.RepositoriesPath = Path.Combine(_root, "repos");
            o.Repositories["ops"] = new PipeScriptRepository { Url = Path.Combine(_root, "origin"), Branch = "main" };
        });
        _services = services.BuildServiceProvider();
        Directory.CreateDirectory(Path.Combine(_root, "jobs"));
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            // git marks its objects read-only
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void Script(string name, string content) => File.WriteAllText(Path.Combine(_root, "jobs", name), content);

    static private object Scripts(params string[] names) => names.Select(n => new { value = n, enabled = true }).ToArray();

    private async Task<PipeExecution> RunAsync(object parameters, Action<PipeWorkflowNode>? configure = null, params JsonObject[] input)
    {
        var workflow = new PipeWorkflowBuilder("Jobs")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("run", RunScripts, parameters, configure)
            .Connect("start", "run")
            .Build();
        var request = new PipeExecutionRequest { InputItems = input.Length > 0 ? input.Select(j => new PipeItem(j)).ToList() : null };
        return await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
    }

    static private List<JsonObject> Results(PipeExecution execution, string port = "main")
        => execution.NodeRuns["run"].Outputs!.TryGetValue(port, out var items) ? items.Select(i => i.Json).ToList() : [];

    // Windows PowerShell has no --version
    static private bool PowerShellAvailable => CanRun("pwsh") || CanRun("powershell", "-NoProfile -Command exit 0");

    static private bool CanRun(string program, string arguments = "--version")
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            process!.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task Batch_scripts_run_in_order_with_item_arguments_and_environment()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd scripts need Windows");
        Script("1-first.bat", "@echo off\r\necho first %1 %JOB_NAME%\r\n");
        Script("2-second.cmd", "@echo off\r\necho %PIPE_WORKFLOW%\r\necho warning 1>&2\r\n");

        var execution = await RunAsync(new
        {
            root = "jobs",
            scripts = Scripts("1-first.bat", "2-second.cmd"),
            arguments = "{{ $json.day }}",
            environment = new Dictionary<string, string> { ["JOB_NAME"] = "nightly" }
        }, input: new JsonObject { ["day"] = "monday" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var results = Results(execution);
        Assert.Equal(["1-first.bat", "2-second.cmd"], results.Select(r => r["script"]!.GetValue<string>()));
        Assert.Equal("first monday nightly", results[0]["stdout"]!.GetValue<string>().Trim());
        Assert.Equal("Jobs", results[1]["stdout"]!.GetValue<string>().Trim());
        Assert.Equal("warning", results[1]["stderr"]!.GetValue<string>().Trim());
        Assert.Contains(execution.NodeRuns["run"].Logs, l => l.Message.Contains("2-second.cmd: warning"));
    }

    [Fact]
    public async Task Exit_code_fails_the_node_stops_and_routes_results_to_the_error_output()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd scripts need Windows");
        Script("a.bat", "@echo off\r\necho ok\r\n");
        Script("b.bat", "@echo off\r\necho broken\r\nexit /b 3\r\n");
        Script("c.bat", "@echo off\r\necho never\r\n");

        var execution = await RunAsync(new { root = "jobs", scripts = Scripts("a.bat", "b.bat", "c.bat") },
            node => node.Settings.OnError = PipeNodeErrorMode.ErrorOutput);

        var results = Results(execution, "error");
        Assert.Equal(["a.bat", "b.bat"], results.Select(r => r["script"]!.GetValue<string>()));
        Assert.True(results[0]["success"]!.GetValue<bool>());
        Assert.Equal(3, results[1]["exitCode"]!.GetValue<int>());
        Assert.Equal("exit code 3", results[1]["reason"]!.GetValue<string>());
        Assert.Contains("b.bat: exit code 3", results[1]["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task Error_pattern_fails_a_script_with_exit_code_zero_and_continue_runs_the_rest()
    {
        Assert.SkipUnless(PowerShellAvailable, "PowerShell is not installed");
        Script("check.ps1", "Write-Output 'step 1'\nWrite-Output 'Error: disk full'\nexit 0\n");
        Script("after.ps1", "Write-Output 'done'\n");

        var execution = await RunAsync(new
        {
            root = "jobs",
            scripts = Scripts("check.ps1", "after.ps1"),
            errorPattern = "^error",
            stopOnFirstFailure = false
        }, node => node.Settings.OnError = PipeNodeErrorMode.Continue);

        var results = Results(execution);
        Assert.Equal(2, results.Count);
        Assert.False(results[0]["success"]!.GetValue<bool>());
        Assert.Equal(0, results[0]["exitCode"]!.GetValue<int>());
        Assert.Equal("Error: disk full", results[0]["matchedLine"]!.GetValue<string>());
        Assert.True(results[1]["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Python_gets_the_item_and_credential_values_are_masked()
    {
        Assert.SkipUnless(CanRun("python"), "Python is not installed");
        await _services.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "db",
            Name = "Database",
            TypeId = Guid.Parse(PipeBuiltInCredentials.BasicAuth),
            Data = new JsonObject { ["userName"] = "admin", ["password"] = "s3cret!" }
        });
        Script("job.py", "import json, os, sys\nitem = json.loads(os.environ['PIPE_ITEM'])\nprint(item['name'], os.environ['PIPE_CREDENTIAL_PASSWORD'])\nprint('ü', file=sys.stderr)\n");

        var execution = await RunAsync(new { root = "jobs", scripts = Scripts("job.py"), credentialId = "db" },
            input: new JsonObject { ["name"] = "Ada" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var result = Assert.Single(Results(execution));
        Assert.Equal("Ada ***", result["stdout"]!.GetValue<string>().Trim());
        Assert.Equal("ü", result["stderr"]!.GetValue<string>().Trim());
        Assert.DoesNotContain(execution.NodeRuns["run"].Logs, l => l.Message.Contains("s3cret!"));
    }

    [Fact]
    public async Task Timeout_ends_the_script()
    {
        Assert.SkipUnless(PowerShellAvailable, "PowerShell is not installed");
        Script("slow.ps1", "Start-Sleep -Seconds 30\n");

        var watch = Stopwatch.StartNew();
        var execution = await RunAsync(new { root = "jobs", scripts = Scripts("slow.ps1"), timeoutMinutes = 0.05 },
            node => node.Settings.OnError = PipeNodeErrorMode.Continue);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(25), $"took {watch.Elapsed}");
        Assert.StartsWith("timed out", Assert.Single(Results(execution))["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Only_switched_on_scripts_inside_the_root_run()
    {
        Script("ok.bat", "@echo off\r\necho ok\r\n");

        var none = await RunAsync(new { root = "jobs", scripts = new[] { new { value = "ok.bat", enabled = false } } });
        var outside = await RunAsync(new { root = "jobs", scripts = Scripts("../evil.bat") });
        var missing = await RunAsync(new { root = "jobs", scripts = Scripts("gone.bat") });
        var unknownRoot = await RunAsync(new { root = "nope", scripts = Scripts("ok.bat") });

        Assert.Contains("No script is switched on", none.NodeRuns["run"].Error);
        Assert.Contains("leaves the script source", outside.NodeRuns["run"].Error);
        Assert.Contains("'gone.bat' is missing", missing.NodeRuns["run"].Error);
        Assert.Contains("Unknown script folder 'nope'", unknownRoot.NodeRuns["run"].Error);
    }

    [Fact]
    public async Task Options_list_runnable_files_of_the_folder_or_a_single_file()
    {
        Directory.CreateDirectory(Path.Combine(_root, "jobs", "nightly"));
        Script("b.ps1", "");
        Script("a.py", "");
        Script("readme.txt", "");
        Script(Path.Combine("nightly", "backup.ps1"), "");

        var registry = _services.GetRequiredService<IPipeNodeRegistry>();
        var descriptor = registry.Find(Guid.Parse(RunScripts))!;

        async Task<string[]> Options(JsonObject parameters)
            => (await registry.GetParameterOptionsAsync(descriptor, "scripts", parameters)).Select(o => o.Value).ToArray();

        Assert.Equal(["a.py", "b.ps1"], await Options(new JsonObject { ["root"] = "jobs" }));
        Assert.Equal(["a.py", "b.ps1", "nightly/backup.ps1"], await Options(new JsonObject { ["root"] = "jobs", ["includeSubfolders"] = true }));
        Assert.Equal(["nightly/backup.ps1"], await Options(new JsonObject { ["root"] = "jobs", ["path"] = "nightly/backup.ps1" }));
        Assert.Equal(["jobs"], (await registry.GetParameterOptionsAsync(descriptor, "root", new JsonObject())).Select(o => o.Value));
    }

    [Fact]
    public async Task Git_repository_is_cloned_updated_and_its_commit_reported()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && CanRun("git"), "needs git (and cmd for the script)");
        var origin = Path.Combine(_root, "origin");
        Git(_root, "init", "-b", "main", origin);
        Git(origin, "config", "user.email", "test@example.com");
        Git(origin, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(origin, "deploy.bat"), "@echo off\r\necho version 1\r\n");
        Git(origin, "add", ".");
        Git(origin, "commit", "-m", "v1");

        var parameters = new { source = "gitRepository", repository = "ops", scripts = Scripts("deploy.bat") };
        var first = await RunAsync(parameters);
        Assert.True(first.Status == PipeExecutionStatus.Success, first.Error);
        Assert.Equal("version 1", Results(first)[0]["stdout"]!.GetValue<string>().Trim());

        File.WriteAllText(Path.Combine(origin, "deploy.bat"), "@echo off\r\necho version 2\r\n");
        Git(origin, "commit", "-am", "v2");
        var head = Git(origin, "rev-parse", "HEAD").Trim();

        var second = await RunAsync(parameters);
        var result = Results(second)[0];
        Assert.Equal("version 2", result["stdout"]!.GetValue<string>().Trim());
        Assert.Equal(head, result["commit"]!.GetValue<string>());
    }

    [Fact]
    public async Task Failing_pull_can_fall_back_to_the_last_checkout()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && CanRun("git"), "needs git (and cmd for the script)");
        var origin = Path.Combine(_root, "origin");
        Git(_root, "init", "-b", "main", origin);
        Git(origin, "config", "user.email", "test@example.com");
        Git(origin, "config", "user.name", "Test");
        File.WriteAllText(Path.Combine(origin, "deploy.bat"), "@echo off\r\necho cached\r\n");
        Git(origin, "add", ".");
        Git(origin, "commit", "-m", "v1");
        Assert.Equal(PipeExecutionStatus.Success, (await RunAsync(new { source = "gitRepository", repository = "ops", scripts = Scripts("deploy.bat") })).Status);

        // the origin disappears: pull fails
        foreach (var file in Directory.EnumerateFiles(origin, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(origin, recursive: true);

        var strict = await RunAsync(new { source = "gitRepository", repository = "ops", scripts = Scripts("deploy.bat") });
        var lenient = await RunAsync(new { source = "gitRepository", repository = "ops", scripts = Scripts("deploy.bat"), useLastCheckoutIfPullFails = true });

        Assert.Contains("git fetch failed", strict.NodeRuns["run"].Error);
        Assert.True(lenient.Status == PipeExecutionStatus.Success, lenient.Error);
        Assert.Equal("cached", Results(lenient)[0]["stdout"]!.GetValue<string>().Trim());
        Assert.Contains(lenient.NodeRuns["run"].Logs, l => l.Message.Contains("ran the last checkout"));
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
}
