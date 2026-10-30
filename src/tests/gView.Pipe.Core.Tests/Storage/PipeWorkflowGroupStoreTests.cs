using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeWorkflowGroupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gview-pipe-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>The memory store (default) and the file store behave the same.</summary>
    public static TheoryData<bool> Stores => new() { false, true };

    private ServiceProvider Create(bool fileStore)
        => PipeTestHost.Create(services =>
        {
            if (fileStore)
            {
                services.AddPipeFileStore(options => options.RootPath = _root);
            }
        });

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Groups_are_listed_by_name_updated_and_deleted(bool fileStore)
    {
        using var services = Create(fileStore);
        var store = services.GetRequiredService<IPipeWorkflowGroupStore>();

        var billing = new PipeWorkflowGroup { Name = " Billing ", Color = PipeWorkflowGroupColors.Green };
        await store.SaveAsync(billing);
        await store.SaveAsync(new PipeWorkflowGroup { Name = "alerts" });

        Assert.NotEmpty(billing.Id);
        Assert.Equal(["alerts", "Billing"], (await store.ListAsync()).Select(g => g.Name));

        billing.Description = "Invoices";
        await store.SaveAsync(billing);
        Assert.Equal("Invoices", (await store.GetAsync(billing.Id))!.Description);

        await store.DeleteAsync(billing.Id);
        Assert.Equal(["alerts"], (await store.ListAsync()).Select(g => g.Name));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task Names_are_unique_ignoring_case_and_required(bool fileStore)
    {
        using var services = Create(fileStore);
        var store = services.GetRequiredService<IPipeWorkflowGroupStore>();
        await store.SaveAsync(new PipeWorkflowGroup { Name = "Billing" });

        var duplicate = await Assert.ThrowsAsync<PipeException>(() => store.SaveAsync(new PipeWorkflowGroup { Name = "BILLING" }));
        var empty = await Assert.ThrowsAsync<PipeException>(() => store.SaveAsync(new PipeWorkflowGroup { Name = "  " }));

        Assert.Contains("already exists", duplicate.Message);
        Assert.Contains("needs a name", empty.Message);
    }

    [Fact]
    public async Task Seeding_copies_the_groups_of_the_samples_once()
    {
        var seed = Directory.CreateDirectory(Path.Combine(_root, "seed")).FullName;
        await File.WriteAllTextAsync(Path.Combine(seed, "sample.json"), """{ "id": "sample", "name": "Sample", "groupId": "samples" }""");
        var groups = Path.Combine(_root, "groups.json");
        await File.WriteAllTextAsync(groups, """[ { "id": "samples", "name": "Samples", "color": "teal" } ]""");
        var root = Path.Combine(_root, "data");

        using var services = PipeTestHost.Create(s => s.AddPipeFileStore(o =>
        {
            o.RootPath = root;
            o.SeedWorkflowsPath = seed;
            o.SeedGroupsPath = groups;
        }));

        var workflow = Assert.Single(await services.GetRequiredService<IPipeWorkflowStore>().ListAsync());
        var group = Assert.Single(await services.GetRequiredService<IPipeWorkflowGroupStore>().ListAsync());
        Assert.Equal(group.Id, workflow.GroupId);
        Assert.Equal("Samples", group.Name);
    }
}
