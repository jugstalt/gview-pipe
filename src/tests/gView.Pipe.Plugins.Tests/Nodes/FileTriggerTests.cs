using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class FileTriggerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gview-pipe-trigger-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content = "x")
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private PipeFolderPoller Poller(string pattern = "*", bool subfolders = false, bool changed = false, List<string>? warnings = null)
        => new(_dir, pattern, subfolders, changed, message => warnings?.Add(message));

    static private List<string> Names(IEnumerable<PipeFolderChange> changes) => changes.Select(c => Path.GetFileName(c.FullPath)).Order().ToList();

    [Fact]
    public void Files_that_exist_at_the_start_trigger_nothing_and_a_new_one_comes_after_two_equal_checks()
    {
        Write("old.csv");
        var poller = Poller();
        Assert.Empty(poller.Poll()); // the baseline

        Write("new.csv");
        Assert.Empty(poller.Poll()); // first sight
        var ready = poller.Poll();   // unchanged since

        var change = Assert.Single(ready);
        Assert.Equal("new.csv", Path.GetFileName(change.FullPath));
        Assert.Equal("created", change.Event);

        poller.Commit(ready);
        Assert.Empty(poller.Poll());
    }

    [Fact]
    public void A_file_that_still_grows_waits_and_a_run_that_did_not_start_offers_the_files_again()
    {
        var poller = Poller();
        poller.Poll();

        var path = Write("big.csv", "a");
        Assert.Empty(poller.Poll());
        File.WriteAllText(path, "ab");
        Assert.Empty(poller.Poll()); // changed since the last check
        var ready = poller.Poll();
        Assert.Single(ready);

        // no Commit: the workflow was busy
        Assert.Single(poller.Poll());
    }

    [Fact]
    public void Changed_files_only_count_when_asked_for()
    {
        var path = Write("a.csv", "1");
        var created = Poller();
        var changed = Poller(changed: true);
        created.Poll();
        changed.Poll();

        File.WriteAllText(path, "12");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        created.Poll();
        changed.Poll();

        Assert.Empty(created.Poll());
        var ready = changed.Poll();
        Assert.Equal("changed", Assert.Single(ready).Event);
    }

    [Fact]
    public void A_deleted_file_that_comes_back_is_new()
    {
        var path = Write("a.csv");
        var poller = Poller();
        poller.Poll();

        File.Delete(path);
        poller.Poll();
        Write("a.csv");
        poller.Poll();

        Assert.Equal("created", Assert.Single(poller.Poll()).Event);
    }

    [Fact]
    public void Pattern_and_subfolders_are_respected()
    {
        var flat = Poller("*.csv");
        var deep = Poller("*.csv", subfolders: true);
        flat.Poll();
        deep.Poll();

        Write("a.csv");
        Write("a.txt");
        Write("sub/b.csv");
        flat.Poll();
        deep.Poll();

        Assert.Equal(["a.csv"], Names(flat.Poll()));
        Assert.Equal(["a.csv", "b.csv"], Names(deep.Poll()));
    }

    [Fact]
    public void A_run_holds_at_most_100_files_and_the_rest_follows()
    {
        var poller = Poller();
        poller.Poll();
        for (var i = 0; i < 105; i++)
        {
            Write($"f{i:000}.csv");
        }
        poller.Poll();

        var first = poller.Poll();
        Assert.Equal(PipeFolderPoller.MaxPerRun, first.Count);
        poller.Commit(first);

        Assert.Equal(5, poller.Poll().Count);
    }

    [Fact]
    public void A_missing_folder_keeps_the_state_and_warns_once()
    {
        var warnings = new List<string>();
        var missing = Path.Combine(_dir, "later");
        var poller = new PipeFolderPoller(missing, "*", false, false, warnings.Add);

        Assert.Empty(poller.Poll());
        Assert.Empty(poller.Poll());
        Assert.Single(warnings);
    }

    [Fact]
    public async Task An_active_workflow_runs_with_one_item_per_new_file()
    {
        Write("old.csv");
        using var services = PluginTestHost.Create(options: o => o.FileRoots["work"] = _dir);

        var workflow = new PipeWorkflowBuilder()
            .Node("trigger", PipeBuiltInNodes.FileTrigger, new { folder = "work", pattern = "*.csv", intervalSeconds = 1 })
            .Node("noop", PipeBuiltInNodes.NoOp)
            .Connect("trigger", "noop")
            .Configure(w => w.Active = true)
            .Build();
        var manager = services.GetRequiredService<IPipeTriggerManager>();
        var problems = await manager.ActivateAsync(workflow);
        Assert.Empty(problems);

        try
        {
            Write("order-1.csv", "a");
            Write("order-2.csv", "b");

            var store = services.GetRequiredService<IPipeExecutionStore>();
            IReadOnlyList<PipeExecution> executions = [];
            for (var i = 0; i < 50 && executions.Count == 0; i++)
            {
                await Task.Delay(200);
                executions = await store.ListAsync(workflow.Id);
            }

            var execution = Assert.Single(executions);
            Assert.Equal(PipeExecutionStatus.Success, execution.Status);
            var items = execution.NodeRuns["noop"].Outputs!["main"].Select(i => i.Json).OrderBy(j => j["name"]!.GetValue<string>()).ToList();
            Assert.Equal(["order-1.csv", "order-2.csv"], items.Select(j => j["name"]!.GetValue<string>()));
            Assert.Equal("created", items[0]["event"]!.GetValue<string>());
            Assert.Equal("order-1.csv", items[0]["path"]!.GetValue<string>());
            Assert.Equal("work", items[0]["folder"]!.GetValue<string>());
        }
        finally
        {
            await manager.DeactivateAsync(workflow.Id);
        }
    }

    [Fact]
    public async Task An_unknown_folder_is_a_problem_of_the_activation()
    {
        using var services = PluginTestHost.Create(options: o => o.FileRoots["work"] = _dir);
        var workflow = new PipeWorkflowBuilder()
            .Node("trigger", PipeBuiltInNodes.FileTrigger, new { folder = "nowhere" })
            .Configure(w => w.Active = true)
            .Build();

        var problems = await services.GetRequiredService<IPipeTriggerManager>().ActivateAsync(workflow);

        Assert.Contains("nowhere", Assert.Single(problems));
    }
}
