using gView.Pipe.Abstraction.Audit;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeAuditLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gview-pipe-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public PipeAuditLogTests()
    {
        _services = PipeTestHost.Create(services => services.AddPipeFileStore(options => options.RootPath = _root));
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private IPipeAuditLog Log => _services.GetRequiredService<IPipeAuditLog>();

    static private PipeAuditEntry Entry(DateTimeOffset time, string actor, PipeAuditArea area, string action, string? objectId = null) => new()
    {
        Timestamp = time, Actor = actor, Area = area, Action = action, ObjectId = objectId
    };

    [Fact]
    public async Task Entries_go_into_month_files_and_come_back_newest_first_with_filters()
    {
        var now = DateTimeOffset.UtcNow;
        await Log.WriteAsync(Entry(now.AddMonths(-2), "ada", PipeAuditArea.Workflow, "saved", "invoice"));
        await Log.WriteAsync(Entry(now.AddMinutes(-2), "grace", PipeAuditArea.Credential, "created"));
        await Log.WriteAsync(Entry(now.AddMinutes(-1), "api:CI", PipeAuditArea.Workflow, "deployed", "invoice"));

        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "audit"), "*.jsonl").Length);
        Assert.Equal(["deployed", "created", "saved"], (await Log.QueryAsync(new PipeAuditQuery())).Select(e => e.Action));
        Assert.Equal(["deployed", "saved"], (await Log.QueryAsync(new PipeAuditQuery { ObjectId = "invoice" })).Select(e => e.Action));
        Assert.Equal(["created"], (await Log.QueryAsync(new PipeAuditQuery { Actor = "GRA" })).Select(e => e.Action));
        Assert.Equal(["deployed", "created"], (await Log.QueryAsync(new PipeAuditQuery { From = now.AddDays(-1) })).Select(e => e.Action));

        // paging: the next page starts before the last row
        var page = await Log.QueryAsync(new PipeAuditQuery { MaxCount = 2 });
        Assert.Equal(["saved"], (await Log.QueryAsync(new PipeAuditQuery { Before = page[^1].Timestamp })).Select(e => e.Action));
    }

    [Fact]
    public async Task Pruning_removes_whole_months()
    {
        var now = DateTimeOffset.UtcNow;
        await Log.WriteAsync(Entry(now.AddMonths(-14), "ada", PipeAuditArea.Login, "signedIn"));
        await Log.WriteAsync(Entry(now, "ada", PipeAuditArea.Login, "signedIn"));

        await Log.PruneAsync(now.AddDays(-365));

        Assert.Single(await Log.QueryAsync(new PipeAuditQuery()));
    }

    [Fact]
    public async Task A_broken_line_doesnt_hide_the_others()
    {
        var now = DateTimeOffset.UtcNow;
        await Log.WriteAsync(Entry(now, "ada", PipeAuditArea.Login, "signedIn"));
        var file = Directory.GetFiles(Path.Combine(_root, "audit"))[0];
        await File.AppendAllTextAsync(file, "{\"timestamp\":\"2026-");

        Assert.Single(await Log.QueryAsync(new PipeAuditQuery()));
    }

    [Fact]
    public void Workflow_changes_are_summarized()
    {
        var before = new PipeWorkflowBuilder("Import", "import")
            .Node("start", "00000000-0000-4000-8000-000000000001")
            .Node("set", "00000000-0000-4000-8000-000000000002", new { a = 1 })
            .Node("old", "00000000-0000-4000-8000-000000000002")
            .Connect("start", "set")
            .Build();
        var after = PipeJsonClone(before);
        after.Nodes.RemoveAll(n => n.Id == "old");
        after.Nodes.Add(new PipeWorkflowNode { Id = "http", Name = "HTTP Request" });
        after.Nodes.Single(n => n.Id == "set").Parameters["a"] = 2;
        after.Connections.Add(new PipeConnection { SourceNodeId = "set", TargetNodeId = "http" });
        after.Active = true;

        var summary = PipeWorkflowChanges.Describe(before, after);

        Assert.Contains("nodes added: HTTP Request", summary);
        Assert.Contains("removed: old", summary);
        Assert.Contains("changed: set (parameters)", summary);
        Assert.Contains("connections +1 -0", summary);
        Assert.Contains("activated", summary);
        Assert.Equal("no changes", PipeWorkflowChanges.Describe(before, PipeJsonClone(before)));
    }

    static private PipeWorkflow PipeJsonClone(PipeWorkflow workflow) => gView.Pipe.Abstraction.Serialization.PipeJson.Clone(workflow);
}
