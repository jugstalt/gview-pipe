using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gview-pipe-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public PipeFileStoreTests()
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

    [Fact]
    public async Task Secret_variable_values_are_encrypted_at_rest()
    {
        var store = _services.GetRequiredService<IPipeWorkflowStore>();
        var workflow = new PipeWorkflowBuilder("Secrets", "secrets").Build();
        workflow.Variables =
        [
            new PipeVariable { Name = "DB_PASSWORD", Secret = true, Values = new() { ["DEFAULT"] = "pw-default", ["PROD"] = "pw-prod" } },
            new PipeVariable { Name = "DB_SERVER", Values = new() { ["DEFAULT"] = "db-plain" } }
        ];
        await store.SaveAsync(workflow);

        var json = await File.ReadAllTextAsync(Path.Combine(_root, "workflows", "secrets.json"));
        Assert.DoesNotContain("pw-prod", json);
        Assert.Contains("db-plain", json);
        // the caller's object is not changed
        Assert.Equal("pw-prod", workflow.Variables[0].Values["PROD"]);

        var loaded = (await store.GetAsync("secrets"))!;
        Assert.Equal("pw-prod", loaded.Variables![0].Values["PROD"]);

        var variables = _services.GetRequiredService<IPipeVariableStore>();
        await variables.SaveAsync([new PipeVariable { Name = "TOKEN", Secret = true, Values = new() { ["DEFAULT"] = "tok-instance" } }]);
        Assert.DoesNotContain("tok-instance", await File.ReadAllTextAsync(Path.Combine(_root, "variables.json")));
        Assert.Equal("tok-instance", (await variables.ListAsync()).Single().Values["DEFAULT"]);
    }

    [Fact]
    public async Task Workflow_round_trip_keeps_unknown_properties()
    {
        var store = _services.GetRequiredService<IPipeWorkflowStore>();
        var workflow = new PipeWorkflowBuilder("Round trip", "round-trip")
            .Node("emit", TestNodeIds.Emit, new { items = new[] { new { a = 1 } } })
            .Build();
        await store.SaveAsync(workflow);

        // a newer version of the app wrote an extra property
        var path = Path.Combine(_root, "workflows", "round-trip.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json["futureFeature"] = new JsonObject { ["enabled"] = true };
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var loaded = await store.GetAsync("round-trip");
        await store.SaveAsync(loaded!);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!;

        Assert.Equal("Round trip", loaded!.Name);
        Assert.NotNull(loaded.CreatedAt);
        Assert.True(saved["futureFeature"]!["enabled"]!.GetValue<bool>());
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task Empty_store_is_seeded_once_and_a_non_empty_store_never()
    {
        var seed = Directory.CreateDirectory(Path.Combine(_root, "seed")).FullName;
        await File.WriteAllTextAsync(Path.Combine(seed, "sample.json"), """{ "id": "sample", "name": "Sample" }""");
        var root = Path.Combine(_root, "seeded");

        using (var services = PipeTestHost.Create(s => s.AddPipeFileStore(o => { o.RootPath = root; o.SeedWorkflowsPath = seed; })))
        {
            var store = services.GetRequiredService<IPipeWorkflowStore>();
            Assert.Equal("Sample", Assert.Single(await store.ListAsync()).Name);

            await store.SaveAsync(new PipeWorkflowBuilder("Mine", "mine").Build());
            await store.DeleteAsync("sample");
        }

        // next start: the store has a workflow, so the deleted sample does not come back
        using (var services = PipeTestHost.Create(s => s.AddPipeFileStore(o => { o.RootPath = root; o.SeedWorkflowsPath = seed; })))
        {
            Assert.Equal("Mine", Assert.Single(await services.GetRequiredService<IPipeWorkflowStore>().ListAsync()).Name);
        }
    }

    [Fact]
    public async Task Invalid_ids_are_rejected()
    {
        var store = _services.GetRequiredService<IPipeWorkflowStore>();

        await Assert.ThrowsAsync<gView.Pipe.Abstraction.PipeException>(() => store.GetAsync("../escape"));
    }

    [Fact]
    public async Task Executions_are_listed_newest_first_and_pruned()
    {
        var store = _services.GetRequiredService<IPipeExecutionStore>();
        var start = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await store.SaveAsync(new PipeExecution
            {
                Id = $"ex{i}",
                WorkflowId = "wf",
                StartedAt = start.AddSeconds(i),
                Status = PipeExecutionStatus.Success
            });
        }

        await store.PruneAsync("wf", keep: 3);
        var executions = await store.ListAsync("wf");

        Assert.Equal(["ex4", "ex3", "ex2"], executions.Select(e => e.Id));
        Assert.NotNull(await store.GetAsync("wf", "ex3"));
        Assert.Null(await store.GetAsync("wf", "ex0"));
    }

    [Fact]
    public async Task Credentials_are_encrypted_at_rest()
    {
        var store = _services.GetRequiredService<IPipeCredentialStore>();
        var credential = new PipeCredential
        {
            Name = "Api",
            TypeId = Guid.NewGuid(),
            Data = new JsonObject { ["token"] = "super-secret-value" }
        };

        await store.SaveAsync(credential);

        var file = await File.ReadAllTextAsync(Path.Combine(_root, "credentials", $"{credential.Id}.json"));
        var loaded = await store.GetAsync(credential.Id);

        Assert.DoesNotContain("super-secret-value", file);
        Assert.Equal("super-secret-value", loaded!.Data["token"]!.GetValue<string>());
        Assert.Equal("Api", Assert.Single(await store.ListAsync()).Name);
    }
}
