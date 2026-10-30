using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Database.Tests;

/// <summary>The SQL node, loaded from a plugin folder (own load context, native SQLite) against a SQLite file.</summary>
public sealed class SqlNodeTests : IAsyncLifetime
{
    private const string SqlNode = "98f2efaf-ce68-4446-9997-46bbca748494";

    private readonly string _folder = Directory.CreateTempSubdirectory("gview-pipe-sql-").FullName;
    private ServiceProvider _services = default!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginAssemblies.Add(typeof(NoOpNode).Assembly);
            options.PluginPath = Path.Combine(AppContext.BaseDirectory, "plugins");
        });
        _services = services.BuildServiceProvider();

        await _services.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "db",
            Name = "Test database",
            TypeId = Guid.Parse(PipeBuiltInCredentials.ConnectionString),
            Data = new JsonObject { ["provider"] = "sqlite", ["connectionString"] = $"Data Source={Path.Combine(_folder, "test.db")};Pooling=False" }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private async Task<PipeExecution> RunAsync(object parameters, params JsonObject[] input)
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("sql", SqlNode, parameters)
            .Connect("start", "sql")
            .Build();
        var request = new PipeExecutionRequest { InputItems = input.Length > 0 ? input.Select(j => new PipeItem(j)).ToList() : null };
        return await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
    }

    static private List<JsonObject> Output(PipeExecution execution)
        => execution.NodeRuns["sql"].Outputs!["main"].Select(i => i.Json).ToList();

    [Fact]
    public void Plugin_is_loaded_into_its_own_load_context()
    {
        var descriptor = _services.GetRequiredService<IPipeNodeRegistry>().Find(Guid.Parse(SqlNode));

        Assert.NotNull(descriptor);
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(descriptor!.NodeType.Assembly));
    }

    [Fact]
    public async Task Execute_per_item_with_parameters_then_query_rows_as_items()
    {
        var create = await RunAsync(new { credentialId = "db", operation = "execute", sql = "CREATE TABLE orders (id INTEGER, customer TEXT, total REAL, paid INTEGER)" });
        Assert.Equal(PipeExecutionStatus.Success, create.Status);

        var insert = await RunAsync(
            new
            {
                credentialId = "db",
                operation = "execute",
                sql = "INSERT INTO orders VALUES (@id, @customer, @total, @paid)",
                parameters = new { id = "{{ $json.id }}", customer = "{{ $json.customer }}", total = "{{ $json.total }}", paid = "{{ $json.paid }}" }
            },
            new JsonObject { ["id"] = 1, ["customer"] = "Ada", ["total"] = 120.5, ["paid"] = true },
            new JsonObject { ["id"] = 2, ["customer"] = "Robert'); DROP TABLE orders;--", ["total"] = 40, ["paid"] = false });
        Assert.Equal(PipeExecutionStatus.Success, insert.Status);
        Assert.Equal([1, 1], Output(insert).Select(j => j["affectedRows"]!.GetValue<int>()));

        var query = await RunAsync(new { credentialId = "db", sql = "SELECT id, customer, total FROM orders WHERE total > @min ORDER BY id", parameters = new { min = 10 } });

        Assert.Equal(PipeExecutionStatus.Success, query.Status);
        var rows = Output(query);
        Assert.Equal([1, 2], rows.Select(j => j["id"]!.GetValue<long>()));
        // the "injection" was stored as plain text
        Assert.Equal(["Ada", "Robert'); DROP TABLE orders;--"], rows.Select(j => j["customer"]!.GetValue<string>()));
        Assert.Equal([120.5, 40.0], rows.Select(j => j["total"]!.GetValue<double>()));
    }

    [Fact]
    public async Task Expressions_in_the_sql_text_are_not_evaluated()
    {
        var execution = await RunAsync(new { credentialId = "db", sql = "SELECT '{{ 1 + 1 }}' AS text" });

        Assert.Equal("{{ 1 + 1 }}", Assert.Single(Output(execution))["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_provider_is_reported()
    {
        await _services.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "no-provider",
            Name = "No provider",
            TypeId = Guid.Parse(PipeBuiltInCredentials.ConnectionString),
            Data = new JsonObject { ["connectionString"] = "Data Source=:memory:" }
        });

        var execution = await RunAsync(new { credentialId = "no-provider", sql = "SELECT 1" });

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("has no provider", execution.NodeRuns["sql"].Error);
    }
}
