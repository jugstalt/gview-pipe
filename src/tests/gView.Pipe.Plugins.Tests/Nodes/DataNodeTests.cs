using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Office;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>Extract from File, Convert to File, Date &amp; Time, Crypto and Compression (ADR 0018).</summary>
public sealed class DataNodeTests : IDisposable
{
    // with the Excel format of the Office plugin
    private readonly ServiceProvider _services = PluginTestHost.Create(options: o => o.PluginAssemblies.Add(typeof(PipeExcelFileFormat).Assembly));

    public void Dispose() => _services.Dispose();

    /// <summary>Runs the workflow with one input item that has the file as attachment <c>data</c>.</summary>
    private async Task<PipeExecution> RunAsync(PipeWorkflowBuilder builder, byte[]? file = null, string fileName = "input.csv", JsonObject? json = null)
    {
        var item = new PipeItem(json ?? new JsonObject());
        var binaries = new Dictionary<string, byte[]>();
        if (file is not null)
        {
            item.Binary["data"] = new PipeBinaryRef("input", fileName, "application/octet-stream", file.Length);
            binaries["input"] = file;
        }
        var execution = await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(builder.Build(),
            new PipeExecutionRequest { InputItems = [item], BinaryContent = binaries });
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        return execution;
    }

    static private PipeWorkflowBuilder Workflow(string id, string type, object parameters) => new PipeWorkflowBuilder().Node(id, type, parameters);

    [Fact]
    public async Task Csv_with_detected_delimiter_and_bom_becomes_items_and_json_round_trips()
    {
        var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("name;city\r\nAda;Wien\r\nGrace;\"New York; NY\"\r\n")).ToArray();
        var workflow = Workflow("csv", PipeBuiltInNodes.ExtractFromFile, new { format = "csv" })
            .Node("json", PipeBuiltInNodes.ConvertToFile, new { format = "json", fileName = "people.json" })
            .Node("back", PipeBuiltInNodes.ExtractFromFile, new { format = "json" })
            .Connect("csv", "json")
            .Connect("json", "back");

        var execution = await RunAsync(workflow, csv);

        var rows = execution.Output("csv");
        Assert.Equal(["Ada", "Grace"], rows.Select(r => r["name"]!.GetValue<string>()));
        Assert.Equal("New York; NY", rows[1]["city"]!.GetValue<string>());
        Assert.Equal("people.json", execution.NodeRuns["json"].Outputs!["main"][0].Binary["data"].FileName);
        Assert.Equal(rows.Select(r => r.ToJsonString()), execution.Output("back").Select(r => r.ToJsonString()));
    }

    [Fact]
    public async Task Rows_go_into_a_field_of_the_input_item_when_asked()
    {
        var execution = await RunAsync(
            Workflow("csv", PipeBuiltInNodes.ExtractFromFile, new { format = "csv", output = "field", field = "orders", header = false }),
            "1,2\n3,4"u8.ToArray(), json: new JsonObject { ["batch"] = 7 });

        var item = Assert.Single(execution.Output("csv"));
        Assert.Equal(7, item["batch"]!.GetValue<int>());
        Assert.Equal("4", item["orders"]![1]!["column2"]!.GetValue<string>());
    }

    [Fact]
    public async Task Xml_becomes_items_with_attributes_and_arrays_and_converts_back()
    {
        var xml = """
            <orders xmlns:x="urn:extra">
              <order id="1"><line>Pen</line><x:note>fast</x:note></order>
              <order id="2"><line>Ink</line><line>Paper</line></order>
            </orders>
            """;
        var workflow = Workflow("xml", PipeBuiltInNodes.ExtractFromFile, new { format = "xml", itemElement = "order", alwaysArray = "line" })
            .Node("file", PipeBuiltInNodes.ConvertToFile, new { format = "xml", rootElement = "orders", itemElement = "order" })
            .Node("text", PipeBuiltInNodes.ExtractFromFile, new { format = "text" })
            .Connect("xml", "file")
            .Connect("file", "text");

        var execution = await RunAsync(workflow, Encoding.UTF8.GetBytes(xml), "orders.xml");

        var orders = execution.Output("xml");
        Assert.Equal("1", orders[0]["@id"]!.GetValue<string>());
        Assert.Equal(["Pen"], orders[0]["line"]!.AsArray().Select(l => l!.GetValue<string>()));
        Assert.Equal("fast", orders[0]["x:note"]!.GetValue<string>());
        Assert.Equal(2, orders[1]["line"]!.AsArray().Count);

        var text = Assert.Single(execution.Output("text"))["text"]!.GetValue<string>();
        Assert.Contains("<order id=\"2\">", text);
        Assert.Contains("<line>Paper</line>", text);
    }

    [Fact]
    public async Task Excel_is_written_with_one_sheet_per_value_and_read_back()
    {
        var csv = "region,product,amount\nNorth,Pen,3\nSouth,Ink,5\nNorth,Paper,7\n"u8.ToArray();
        var workflow = Workflow("csv", PipeBuiltInNodes.ExtractFromFile, new { format = "csv" })
            .Node("excel", PipeBuiltInNodes.ConvertToFile, new { format = "xlsx", sheetPerField = "region", fileName = "sales.xlsx" })
            .Node("north", PipeBuiltInNodes.ExtractFromFile, new { format = "xlsx", sheet = "North" })
            .Connect("csv", "excel")
            .Connect("excel", "north");

        var execution = await RunAsync(workflow, csv);

        Assert.Equal("sales.xlsx", execution.NodeRuns["excel"].Outputs!["main"][0].Binary["data"].FileName);
        var north = execution.Output("north");
        Assert.Equal(["Pen", "Paper"], north.Select(r => r["product"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Unknown_formats_and_missing_attachments_are_reported()
    {
        var execution = await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(
            Workflow("x", PipeBuiltInNodes.ExtractFromFile, new { format = "csv" }).Build());

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("no attachment 'data'", execution.Error);
    }

    [Theory]
    [InlineData("format", "2026-11-02T07:30:00Z", "Europe/Vienna", "dd.MM.yyyy HH:mm", null, "02.11.2026 08:30")]
    [InlineData("parse", "02.11.2026 08:30", "Europe/Vienna", "dd.MM.yyyy HH:mm", null, "2026-11-02T08:30:00+01:00")]
    [InlineData("add", "2026-01-31T00:00:00+00:00", null, null, "months", "2026-02-28T00:00:00+00:00")]
    [InlineData("startOf", "2026-11-05T13:45:00+01:00", null, null, "weeks", "2026-11-02T00:00:00+01:00")]
    [InlineData("convertTimeZone", "2026-07-01T12:00:00Z", "Europe/Vienna", null, null, "2026-07-01T14:00:00+02:00")]
    public async Task Date_and_time_operations(string operation, string value, string? timeZone, string? pattern, string? unit, string expected)
    {
        var execution = await RunAsync(Workflow("date", PipeBuiltInNodes.DateTime,
            new { operation, value, timeZone, pattern, unit = unit ?? "days", amount = 1, targetField = "result" }));

        Assert.Equal(expected, Assert.Single(execution.Output("date"))["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task Date_difference_counts_days_and_whole_months()
    {
        var days = await RunAsync(Workflow("d", PipeBuiltInNodes.DateTime,
            new { operation = "difference", value = "2026-01-01T00:00:00Z", until = "2026-01-03T12:00:00Z", unit = "days", targetField = "d" }));
        var months = await RunAsync(Workflow("d", PipeBuiltInNodes.DateTime,
            new { operation = "difference", value = "2026-01-31", until = "2026-03-30", unit = "months", targetField = "d" }));

        Assert.Equal(2.5, days.Output("d")[0]["d"]!.GetValue<double>());
        Assert.Equal(1, months.Output("d")[0]["d"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("hash", "sha256", "abc", null, "hex", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("hash", "md5", "abc", null, "hex", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("hmac", "sha256", "The quick brown fox jumps over the lazy dog", "key", "hex", "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8")]
    [InlineData("base64Encode", "sha256", "gView ✓", null, "hex", "Z1ZpZXcg4pyT")]
    [InlineData("base64Decode", "sha256", "Z1ZpZXcg4pyT", null, "hex", "gView ✓")]
    public async Task Crypto_operations(string operation, string algorithm, string value, string? secret, string output, string expected)
    {
        var execution = await RunAsync(Workflow("c", PipeBuiltInNodes.Crypto, new { operation, algorithm, value, secret, output, targetField = "r" }));

        Assert.Equal(expected, execution.Output("c")[0]["r"]!.GetValue<string>());
    }

    [Fact]
    public async Task Hash_of_an_attachment()
    {
        var execution = await RunAsync(Workflow("c", PipeBuiltInNodes.Crypto, new { operation = "hash", source = "binary", targetField = "r" }), "abc"u8.ToArray());

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", execution.Output("c")[0]["r"]!.GetValue<string>());
    }

    [Fact]
    public async Task Zip_and_unzip_round_trip_and_gzip_too()
    {
        var workflow = Workflow("zip", PipeBuiltInNodes.Compression, new { operation = "zip", fileName = "pack.zip" })
            .Node("unzip", PipeBuiltInNodes.Compression, new { operation = "unzip" })
            .Node("gzip", PipeBuiltInNodes.Compression, new { operation = "gzip" })
            .Node("gunzip", PipeBuiltInNodes.Compression, new { operation = "gunzip" })
            .Node("text", PipeBuiltInNodes.ExtractFromFile, new { format = "text", output = "field", field = "content" })
            .Connect("zip", "unzip")
            .Connect("unzip", "gzip")
            .Connect("gzip", "gunzip")
            .Connect("gunzip", "text");

        var execution = await RunAsync(workflow, "hello zip"u8.ToArray(), "hello.txt");

        Assert.Equal(1, execution.Output("zip")[0]["entries"]!.GetValue<int>());
        var entry = Assert.Single(execution.Output("unzip"));
        Assert.Equal("hello.txt", entry["path"]!.GetValue<string>());
        Assert.Equal("hello.txt.gz", execution.NodeRuns["gzip"].Outputs!["main"][0].Binary["data"].FileName);
        Assert.Equal("hello zip", execution.Output("text")[0]["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unzipping_stops_at_the_size_limit()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var entry = archive.CreateEntry("big.txt", CompressionLevel.SmallestSize).Open();
            await entry.WriteAsync(new byte[3 * 1024 * 1024]);
        }

        var item = new PipeItem();
        item.Binary["data"] = new PipeBinaryRef("zip", "big.zip", "application/zip", buffer.Length);
        var execution = await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(
            Workflow("unzip", PipeBuiltInNodes.Compression, new { operation = "unzip", maxMegabytes = 2 }).Build(),
            new PipeExecutionRequest { InputItems = [item], BinaryContent = new Dictionary<string, byte[]> { ["zip"] = buffer.ToArray() } });

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("larger than the allowed size", execution.Error);
    }
}
