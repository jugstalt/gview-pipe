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
    private const string RunScripts = PipeScriptNodes.RunScriptsFolder;

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
            options.PluginAssemblies.Add(typeof(RunScriptsFolderNode).Assembly);
            // instance variables reach every script; JOB_NAME is overridden by the node in some tests
            options.Variables["JOB_NAME"] = new() { ["DEFAULT"] = "from-instance" };
            options.Variables["JOB_TOKEN"] = new() { ["DEFAULT"] = "tok-12345" };
            options.SecretVariables.Add("JOB_TOKEN");
        });
        services.Configure<PipeScriptsOptions>(o =>
        {
            o.ScriptRoots["jobs"] = Path.Combine(_root, "jobs");
            o.RepositoriesPath = Path.Combine(_root, "repos");
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

    private Task<PipeExecution> RunAsync(object parameters, Action<PipeWorkflowNode>? configure = null, params JsonObject[] input)
        => RunNodeAsync(RunScripts, parameters, configure, input);

    private async Task<PipeExecution> RunNodeAsync(string type, object parameters, Action<PipeWorkflowNode>? configure = null, params JsonObject[] input)
    {
        var workflow = new PipeWorkflowBuilder("Jobs")
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("run", type, parameters, configure)
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
    public async Task Variables_reach_the_script_and_secret_ones_are_masked()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd scripts need Windows");
        Script("vars.bat", "@echo off\r\necho %JOB_NAME% %PIPE_ENVIRONMENT% %JOB_TOKEN%\r\n");

        var execution = await RunAsync(new { root = "jobs", scripts = Scripts("vars.bat") });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("from-instance DEFAULT ***", Results(execution)[0]["stdout"]!.GetValue<string>().Trim());
        Assert.DoesNotContain(execution.NodeRuns["run"].Logs, l => l.Message.Contains("tok-12345"));
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
        Assert.Contains("leaves the script folder", outside.NodeRuns["run"].Error);
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
    public async Task Run_script_runs_one_file_from_a_subfolder_in_its_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd scripts need Windows");
        Directory.CreateDirectory(Path.Combine(_root, "jobs", "nightly"));
        Script(Path.Combine("nightly", "where.bat"), "@echo off\r\necho %CD%\r\n");

        var execution = await RunNodeAsync(PipeScriptNodes.RunScript, new { root = "jobs", script = "nightly/where.bat" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var result = Assert.Single(Results(execution));
        Assert.Equal("nightly/where.bat", result["script"]!.GetValue<string>());
        Assert.Equal(Path.Combine(_root, "jobs", "nightly"), result["stdout"]!.GetValue<string>().Trim(), ignoreCase: true);
    }

    [Fact]
    public async Task Run_script_offers_all_runnable_files_of_the_folder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "jobs", "nightly"));
        Script("a.py", "");
        Script("notes.txt", "");
        Script(Path.Combine("nightly", "b.ps1"), "");
        var registry = _services.GetRequiredService<IPipeNodeRegistry>();

        var options = await registry.GetParameterOptionsAsync(registry.Find(Guid.Parse(PipeScriptNodes.RunScript))!, "script", new JsonObject { ["root"] = "jobs" });

        Assert.Equal(["a.py", "nightly/b.ps1"], options.Select(o => o.Value));
    }

    [Fact]
    public async Task Version_1_nodes_with_a_git_source_ask_for_run_scripts_repo()
    {
        var execution = await RunAsync(new { source = "gitRepository", repository = "ops", scripts = Scripts("deploy.bat") },
            node => node.TypeVersion = 1);

        Assert.Contains("Run Scripts Repo", execution.NodeRuns["run"].Error);
    }
}
