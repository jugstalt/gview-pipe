using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Core.Options;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Common;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Read File and Write File inside released folders, and CSV.</summary>
public sealed class FileNodeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gview-pipe-files-").FullName;
    private readonly ServiceProvider _services;

    public FileNodeTests()
    {
        _services = PluginTestHost.Create(options: o => o.FileRoots["work"] = _root);
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private async Task<PipeExecution> RunAsync(Func<PipeWorkflowBuilder, PipeWorkflowBuilder> nodes, params JsonObject[] input)
    {
        var workflow = nodes(new PipeWorkflowBuilder().Node("start", PipeBuiltInNodes.ManualTrigger)).Build();
        var request = new PipeExecutionRequest { InputItems = input.Length > 0 ? input.Select(j => new PipeItem(j)).ToList() : null };
        return await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
    }

    [Fact]
    public async Task Items_written_as_csv_are_read_back_as_items()
    {
        var execution = await RunAsync(b => b
                .Node("write", PipeBuiltInNodes.WriteFile, new { folder = "work", path = "out/orders.csv", content = "itemsAsCsv", csvDelimiter = ";" })
                .Node("read", PipeBuiltInNodes.ReadFile, new { folder = "work", path = "out/*.csv", format = "csv", csvDelimiter = ";" })
                .Connect("start", "write")
                .Connect("write", "read"),
            new JsonObject { ["id"] = 1, ["note"] = "semi;colon \"quoted\"" },
            new JsonObject { ["id"] = 2, ["extra"] = "late column" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("out/orders.csv", execution.Output("write")[0]["path"]!.GetValue<string>());
        Assert.Equal(
            ["""{"id":"1","note":"semi;colon \u0022quoted\u0022","extra":""}""", """{"id":"2","note":"","extra":"late column"}"""],
            execution.Output("read").Select(j => j.ToJsonString()));
    }

    [Fact]
    public async Task Csv_append_adds_rows_without_a_second_header()
    {
        object Write(string ifExists) => new { folder = "work", path = "log.csv", content = "itemsAsCsv", ifExists };

        await RunAsync(b => b.Node("write", PipeBuiltInNodes.WriteFile, Write("overwrite")).Connect("start", "write"), new JsonObject { ["n"] = 1 });
        await RunAsync(b => b.Node("write", PipeBuiltInNodes.WriteFile, Write("append")).Connect("start", "write"), new JsonObject { ["n"] = 2 });

        Assert.Equal("n\r\n1\r\n2\r\n", File.ReadAllText(Path.Combine(_root, "log.csv")));
    }

    [Fact]
    public async Task Text_per_item_and_binary_read_round_trip()
    {
        var execution = await RunAsync(b => b
                .Node("write", PipeBuiltInNodes.WriteFile, new { folder = "work", path = "notes/{{ $json.name }}.txt", content = "text", text = "Hello {{ $json.name }}" })
                .Node("read", PipeBuiltInNodes.ReadFile, new { folder = "work", path = "notes/ada.txt" })
                .Node("copy", PipeBuiltInNodes.WriteFile, new { folder = "work", path = "copy/{{ $json.fileName }}", content = "binary" })
                .Connect("start", "write")
                .Connect("write", "read")
                .Connect("read", "copy"),
            new JsonObject { ["name"] = "ada" },
            new JsonObject { ["name"] = "grace" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("notes/grace.txt", execution.Output("write")[1]["writtenFile"]!["path"]!.GetValue<string>());
        // read runs once per input item: both read ada.txt
        Assert.Equal(2, execution.Output("read").Count);
        Assert.Equal("Hello ada", File.ReadAllText(Path.Combine(_root, "copy", "ada.txt")));
    }

    [Fact]
    public async Task Json_file_becomes_items()
    {
        File.WriteAllText(Path.Combine(_root, "list.json"), """[ { "a": 1 }, 2 ]""");

        var execution = await RunAsync(b => b.Node("read", PipeBuiltInNodes.ReadFile, new { folder = "work", path = "list.json", format = "json" }).Connect("start", "read"));

        Assert.Equal(["""{"a":1}""", """{"value":2}"""], execution.Output("read").Select(j => j.ToJsonString()));
    }

    [Theory]
    [InlineData("work", "../outside.txt", "leaves the folder")]
    [InlineData("work", "C:/Windows/win.ini", "must be relative")]
    [InlineData("other", "a.txt", "Unknown folder 'other'")]
    [InlineData("work", "missing.txt", "not found")]
    public async Task Paths_outside_released_folders_are_rejected(string folder, string path, string error)
    {
        var execution = await RunAsync(b => b.Node("read", PipeBuiltInNodes.ReadFile, new { folder, path }).Connect("start", "read"));

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains(error, execution.NodeRuns["read"].Error);
    }

    [Fact]
    public async Task Existing_file_fails_when_asked_to()
    {
        File.WriteAllText(Path.Combine(_root, "taken.txt"), "x");

        var execution = await RunAsync(b => b.Node("write", PipeBuiltInNodes.WriteFile, new { folder = "work", path = "taken.txt", content = "text", text = "y", ifExists = "fail" }).Connect("start", "write"));

        Assert.Contains("already exists", execution.NodeRuns["write"].Error);
        Assert.Equal("x", File.ReadAllText(Path.Combine(_root, "taken.txt")));
    }

    [Fact]
    public void Csv_handles_quotes_line_breaks_and_tabs()
    {
        var rows = PipeCsv.Parse("name\tnote\r\nAda\t\"two\nlines, \"\"quoted\"\"\"\r\n", PipeCsv.Delimiter(@"\t"));

        var row = Assert.Single(rows);
        Assert.Equal("two\nlines, \"quoted\"", row["note"]!.GetValue<string>());
    }
}
