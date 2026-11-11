using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.AI;
using gView.Pipe.Plugins.Nodes.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

/// <summary>
/// Every node of the repository explains itself (ADR 0043): descriptions of nodes, parameters and enum values,
/// at least one example, and the verified examples produce the output they show.
/// </summary>
public sealed class NodeGuideTests
{
    /// <summary>All plugins of the repository with nodes; the Office plugin adds the Excel format.</summary>
    static private readonly Assembly[] Plugins =
    [
        typeof(NoOpNode).Assembly,
        typeof(AiNode).Assembly,
        typeof(gView.Pipe.Plugins.AzureStorage.AzureBlobNode).Assembly,
        typeof(gView.Pipe.Plugins.Sftp.SftpNode).Assembly,
        typeof(gView.Pipe.Plugins.Database.SqlNode).Assembly,
        typeof(gView.Pipe.Plugins.Scripts.RunScriptNode).Assembly,
        typeof(gView.Pipe.Plugins.Office.PipeExcelFileFormat).Assembly
    ];

    static private readonly Lazy<IReadOnlyList<PipeNodeDescriptor>> AllNodes = new(() =>
    {
        using var services = Create(_ => { }, _ => { });
        return services.GetRequiredService<IPipeNodeRegistry>().Nodes.Where(n => !n.Obsolete).OrderBy(n => n.Name).ToList();
    });

    static private ServiceProvider Create(Action<IServiceCollection> configure, Action<gView.Pipe.Core.Options.PipeCoreOptions> options)
        => PluginTestHost.Create(configure, o =>
        {
            foreach (var plugin in Plugins.Skip(1))
            {
                o.PluginAssemblies.Add(plugin);
            }
            options(o);
        });

    [Fact]
    public void Every_node_parameter_and_choice_has_a_description()
    {
        var missing = new List<string>();
        foreach (var node in AllNodes.Value)
        {
            if (string.IsNullOrWhiteSpace(node.Description))
            {
                missing.Add($"{node.Name}: node");
            }
            if (node.ParametersType is null)
            {
                continue;
            }

            foreach (var property in node.ParametersType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
            {
                if (string.IsNullOrWhiteSpace(property.GetCustomAttribute<PipeParameterAttribute>()?.Description))
                {
                    missing.Add($"{node.Name}: {property.Name}");
                }

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (type.IsEnum)
                {
                    missing.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Where(f => string.IsNullOrWhiteSpace(f.GetCustomAttribute<PipeEnumValueAttribute>()?.Description))
                        .Select(f => $"{node.Name}: {property.Name} = {f.Name}"));
                }

                if (property.GetCustomAttribute<PipeParameterAttribute>() is { ShowWhen: { } showWhen }
                    && node.ParametersType.GetProperty(showWhen) is null)
                {
                    missing.Add($"{node.Name}: {property.Name} shows when the unknown property {showWhen}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Missing descriptions:\n" + string.Join("\n", missing.Distinct()));
    }

    [Fact]
    public void Every_node_has_an_example()
    {
        var missing = AllNodes.Value.Where(n => n.Examples.Count == 0).Select(n => n.Name).ToList();
        Assert.True(missing.Count == 0, "Nodes without examples:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void The_examples_of_all_nodes_load()
    {
        using var services = Create(_ => { }, _ => { });
        var warnings = services.GetRequiredService<gView.Pipe.Core.Plugins.PipePluginCatalog>().Warnings.Where(w => w.StartsWith("Examples of")).ToList();
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));
    }

    /// <summary>Lists the examples that are only shown (triggers, nodes without a fake); never fails.</summary>
    [Fact]
    public void Unverified_examples_are_listed()
    {
        var shown = AllNodes.Value.SelectMany(n => n.Examples.Where(e => !e.Verified || n.IsTrigger).Select(e => $"{n.Name}: {e.Title}")).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"{shown.Count} examples are shown only:\n" + string.Join("\n", shown));
    }

    static public TheoryData<string, int> VerifiedExamples()
    {
        var data = new TheoryData<string, int>();
        foreach (var node in AllNodes.Value.Where(n => !n.IsTrigger))
        {
            for (var i = 0; i < node.Examples.Count; i++)
            {
                if (node.Examples[i].Verified)
                {
                    data.Add(node.Name, i);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(VerifiedExamples))]
    public async Task Example_produces_the_shown_output(string nodeName, int index)
    {
        var node = AllNodes.Value.Single(n => n.Name == nodeName);
        var example = node.Examples[index];

        var root = Directory.CreateTempSubdirectory("gview-pipe-example-").FullName;
        try
        {
            if (example.Given?["files"] is JsonObject files)
            {
                foreach (var (path, content) in files)
                {
                    var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    await File.WriteAllTextAsync(file, content!.GetValue<string>().Replace("\r\n", "\n"));
                }
            }

            var llm = new ExampleLlm(example.Given?["ai"]);
            using var services = Create(
                s =>
                {
                    s.RemoveAll<IPipeLlmProvider>();
                    s.AddSingleton<IPipeLlmProvider>(llm);
                },
                o => o.FileRoots["files"] = root);

            var parameters = (JsonObject)example.Parameters.DeepClone();
            await AddCredentialsAsync(services, node, parameters);

            var store = new MemoryBinaryStore();
            var builder = new PipeWorkflowBuilder()
                .Node("start", PipeBuiltInNodes.ManualTrigger)
                .Node("node", node.Id.ToString(), parameters);
            var pinned = new Dictionary<string, Dictionary<string, List<PipeItem>>>();
            var inputs = example.Input.Count == 0 ? new Dictionary<string, IReadOnlyList<PipeExampleItem>> { [PipePorts.Main] = [] } : example.Input;
            foreach (var (port, items) in inputs)
            {
                if (port == PipePorts.Main)
                {
                    builder.Connect("start", "node");
                    continue;
                }
                // other input ports are fed by a pinned node
                builder.Node("in-" + port, PipeBuiltInNodes.NoOp).Connect("start", "in-" + port).Connect("in-" + port, "node", targetPort: port);
                pinned["in-" + port] = new() { [PipePorts.Main] = await ItemsAsync(store, items) };
            }
            var workflow = builder.Configure(w => w.PinData = pinned.Count > 0 ? pinned : null).Build();

            var request = new PipeExecutionRequest
            {
                // no input: one empty item, as a manual run starts
                InputItems = inputs.GetValueOrDefault(PipePorts.Main) is { Count: > 0 } main ? await ItemsAsync(store, main) : null,
                BinaryStore = store
            };
            var execution = await services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request);
            Assert.True(execution.Status == PipeExecutionStatus.Success, $"{nodeName} / {example.Title}: {execution.Error}");

            var outputs = execution.NodeRuns["node"].Outputs ?? new();
            foreach (var (port, expected) in example.Output)
            {
                var actual = outputs.GetValueOrDefault(port) ?? [];
                // content is compared only where the example shows it as text
                bool HasText(int index, string name)
                    => index < expected.Count && expected[index].Binary.TryGetValue(name, out var file) && file.Text is not null;

                Assert.Equal(
                    expected.Select(i => Comparable(i.Json, i.Binary.ToDictionary(b => b.Key, b => (b.Value.FileName, b.Value.Text)), example.Ignore)).ToList(),
                    actual.Select((i, index) => Comparable(i.Json,
                        i.Binary.ToDictionary(b => b.Key, b => (b.Value.FileName, HasText(index, b.Key) ? store.Text(b.Value) : null)), example.Ignore)).ToList());
            }
            foreach (var (port, items) in outputs.Where(o => !example.Output.ContainsKey(o.Key)))
            {
                Assert.True(items.Count == 0, $"{nodeName} / {example.Title}: {items.Count} items on '{port}', which the example doesn't show");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A credential of the first allowed type for every credential parameter the example leaves empty.</summary>
    static private async Task AddCredentialsAsync(ServiceProvider services, PipeNodeDescriptor node, JsonObject parameters)
    {
        foreach (var property in node.ParametersType?.GetProperties() ?? [])
        {
            var key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (property.GetCustomAttribute<PipeCredentialAttribute>() is { CredentialTypeIds.Length: > 0 } credential && parameters[key] is null)
            {
                var stored = new PipeCredential { Name = "Example", TypeId = credential.CredentialTypeIds[0], Data = new JsonObject { ["apiKey"] = "example" } };
                await services.GetRequiredService<IPipeCredentialStore>().SaveAsync(stored);
                parameters[key] = stored.Id;
            }
        }
    }

    static private async Task<List<PipeItem>> ItemsAsync(MemoryBinaryStore store, IReadOnlyList<PipeExampleItem> items)
    {
        var result = new List<PipeItem>();
        foreach (var item in items)
        {
            var pipeItem = new PipeItem((JsonObject)item.Json.DeepClone());
            foreach (var (name, file) in item.Binary)
            {
                pipeItem.Binary[name] = await store.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes((file.Text ?? "").Replace("\r\n", "\n"))), file.FileName, file.MimeType);
            }
            result.Add(pipeItem);
        }
        return result;
    }

    /// <summary>The item as text without the ignored fields; attachments by name, file name and text.</summary>
    static private string Comparable(JsonObject json, Dictionary<string, (string FileName, string? Text)> binary, IReadOnlyList<string> ignore)
    {
        var copy = (JsonObject)json.DeepClone();
        foreach (var path in ignore)
        {
            var parts = path.Split('.');
            var parent = parts[..^1].Aggregate((JsonNode?)copy, (node, part) => node?[part]);
            (parent as JsonObject)?.Remove(parts[^1]);
        }
        var files = string.Join(", ", binary.OrderBy(b => b.Key).Select(b => $"{b.Key}={b.Value.FileName}:{b.Value.Text?.Replace("\r\n", "\n").TrimEnd()}"));
        return copy.ToJsonString() + (files.Length > 0 ? " + " + files : "");
    }

    /// <summary>Answers the AI node with <c>given.ai.answer</c> (text, or an object as JSON).</summary>
    private sealed class ExampleLlm(JsonNode? given) : IPipeLlmProvider
    {
        public string CredentialTypeId => AnthropicCredential.TypeId;

        public Task<PipeLlmResponse> CompleteAsync(JsonObject credential, PipeLlmRequest request, CancellationToken cancellationToken)
        {
            var answer = given?["answer"] switch
            {
                JsonValue text when text.TryGetValue<string>(out var s) => s,
                { } other => other.ToJsonString(),
                null => ""
            };
            return Task.FromResult(new PipeLlmResponse(answer, "end_turn", request.Model, 100, 20));
        }
    }

    private sealed class MemoryBinaryStore : IPipeBinaryStore
    {
        private readonly ConcurrentDictionary<string, byte[]> _content = new();

        public async Task<PipeBinaryRef> StoreAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var id = Guid.NewGuid().ToString("N");
            _content[id] = buffer.ToArray();
            return new PipeBinaryRef(id, fileName, mimeType, buffer.Length);
        }

        public Task<Stream> OpenReadAsync(PipeBinaryRef reference, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(_content[reference.Id], writable: false));

        public string Text(PipeBinaryRef reference) => _content.TryGetValue(reference.Id, out var bytes) ? Encoding.UTF8.GetString(bytes) : "";
    }
}
