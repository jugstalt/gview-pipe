using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeWorkflowVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gview-pipe-versions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private ServiceProvider Host(int versions = 50)
        => PipeTestHost.Create(services => services.AddPipeFileStore(options =>
        {
            options.RootPath = _root;
            options.WorkflowVersions = versions;
        }));

    [Fact]
    public async Task Every_changing_save_adds_a_version_and_a_plain_resave_does_not()
    {
        using var host = Host();
        var store = host.GetRequiredService<IPipeWorkflowStore>();
        var versions = host.GetRequiredService<IPipeWorkflowVersionStore>();

        var workflow = new PipeWorkflow { Id = "w", Name = "First" };
        await store.SaveAsync(workflow);
        await store.SaveAsync(workflow); // only updatedAt differs
        await Task.Delay(5);
        workflow.Name = "Second";
        await store.SaveAsync(workflow);

        var list = await versions.ListAsync("w");
        Assert.Equal(2, list.Count);
        Assert.Equal("first version", list[1].Summary);
        Assert.Contains("renamed from 'First'", list[0].Summary);
    }

    [Fact]
    public async Task A_version_can_be_read_back_and_the_oldest_ones_are_pruned()
    {
        using var host = Host(versions: 2);
        var store = host.GetRequiredService<IPipeWorkflowStore>();
        var versions = host.GetRequiredService<IPipeWorkflowVersionStore>();

        var workflow = new PipeWorkflow { Id = "w", Name = "v1" };
        foreach (var name in new[] { "v1", "v2", "v3" })
        {
            workflow.Name = name;
            await store.SaveAsync(workflow);
            await Task.Delay(5);
        }

        var list = await versions.ListAsync("w");
        Assert.Equal(2, list.Count);
        var newest = await versions.GetAsync("w", list[0].Id);
        Assert.Equal("v3", newest!.Name);
        var older = await versions.GetAsync("w", list[1].Id);
        Assert.Equal("v2", older!.Name);
    }

    [Fact]
    public async Task Deleting_the_workflow_deletes_its_versions()
    {
        using var host = Host();
        var store = host.GetRequiredService<IPipeWorkflowStore>();
        var versions = host.GetRequiredService<IPipeWorkflowVersionStore>();

        await store.SaveAsync(new PipeWorkflow { Id = "w", Name = "W" });
        await store.DeleteAsync("w");

        Assert.Empty(await versions.ListAsync("w"));
    }

    [Fact]
    public async Task Zero_versions_switches_the_history_off()
    {
        using var host = Host(versions: 0);
        await host.GetRequiredService<IPipeWorkflowStore>().SaveAsync(new PipeWorkflow { Id = "w", Name = "W" });

        Assert.Empty(await host.GetRequiredService<IPipeWorkflowVersionStore>().ListAsync("w"));
    }
}
