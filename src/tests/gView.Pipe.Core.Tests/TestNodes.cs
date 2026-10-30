using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Triggers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests;

static public class TestNodeIds
{
    public const string Emit = "00000000-0000-4000-8000-000000000001";
    public const string Echo = "00000000-0000-4000-8000-000000000002";
    public const string Fail = "00000000-0000-4000-8000-000000000003";
    public const string Slow = "00000000-0000-4000-8000-000000000004";
    public const string Tag = "00000000-0000-4000-8000-000000000005";
    public const string Trigger = "00000000-0000-4000-8000-000000000006";
    public const string Versioned = "00000000-0000-4000-8000-000000000007";
    public const string BinaryLength = "00000000-0000-4000-8000-000000000008";
    public const string Chatty = "00000000-0000-4000-8000-000000000009";
    public const string Unknown = "00000000-0000-4000-8000-0000000000ff";
}

/// <summary>Reads every binary attachment from the binary store and writes its length into the item.</summary>
[PipeNode(TestNodeIds.BinaryLength, "Binary Length")]
public sealed class BinaryLengthNode : IPipeNode
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        foreach (var item in items)
        {
            foreach (var (name, reference) in item.Binary)
            {
                await using var stream = await context.BinaryStore.OpenReadAsync(reference, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                item.Json[$"{name}Length"] = buffer.Length;
            }
        }
        return PipeNodeResult.FromMain(items);
    }
}

public sealed class EmitParameters
{
    public List<JsonObject> Items { get; set; } = new();
}

/// <summary>Ignores its input and emits the configured items.</summary>
[PipeNode(TestNodeIds.Emit, "Emit")]
public sealed class EmitNode : IPipeNode<EmitParameters>
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<EmitParameters>();
        return Task.FromResult(PipeNodeResult.FromMain(parameters.Items.Select(i => new PipeItem((JsonObject)i.DeepClone()))));
    }
}

/// <summary>Returns the items of all input ports on main.</summary>
[PipeNode(TestNodeIds.Echo, "Echo", Inputs = new[] { "main", "second" })]
public sealed class EchoNode : IPipeNode
{
    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.InputPorts.SelectMany(p => context.GetInput(p))));
}

public sealed class FailParameters
{
    public string Key { get; set; } = "";
    public int FailTimes { get; set; } = int.MaxValue;
}

/// <summary>Throws for the first <c>FailTimes</c> calls per key, then echoes.</summary>
[PipeNode(TestNodeIds.Fail, "Fail")]
public sealed class FailNode : IPipeNode<FailParameters>
{
    static public readonly ConcurrentDictionary<string, int> Calls = new();

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<FailParameters>();
        var call = Calls.AddOrUpdate(parameters.Key, 1, (_, c) => c + 1);
        if (call <= parameters.FailTimes)
        {
            throw new InvalidOperationException($"boom {call}");
        }
        return Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
    }
}

/// <summary>Logs three lines with a pause between them, like a script printing its progress.</summary>
[PipeNode(TestNodeIds.Chatty, "Chatty")]
public sealed class ChattyNode : IPipeNode
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        for (var i = 1; i <= 3; i++)
        {
            context.Logger.LogInformation("step {Step}", i);
            await Task.Delay(30, cancellationToken);
        }
        return PipeNodeResult.FromMain(context.GetInput());
    }
}

public sealed class SlowParameters
{
    public int DelayMs { get; set; }
}

[PipeNode(TestNodeIds.Slow, "Slow")]
public sealed class SlowNode : IPipeNode<SlowParameters>
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(context.GetParameters<SlowParameters>().DelayMs, cancellationToken);
        return PipeNodeResult.FromMain(context.GetInput());
    }
}

public sealed class TagParameters
{
    public string Field { get; set; } = "tag";
    public JsonNode? Value { get; set; }
}

/// <summary>Sets one field per item; the value may be an expression.</summary>
[PipeNode(TestNodeIds.Tag, "Tag")]
public sealed class TagNode : PipeItemNodeBase<TagParameters>
{
    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, TagParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        item.Json[parameters.Field] = parameters.Value?.DeepClone();
        return Task.FromResult<PipeItem?>(item);
    }
}

[PipeNode(TestNodeIds.Trigger, "Test Trigger")]
public sealed class TestTriggerNode : IPipeTriggerNode
{
    static public readonly ConcurrentDictionary<string, IPipeTriggerContext> Activated = new();

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        Activated[context.WorkflowId] = context;
        return Task.FromResult<IAsyncDisposable?>(null);
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
}

public sealed class VersionedParameters
{
    public string NewName { get; set; } = "";
}

/// <summary>Version 2 renamed parameter <c>oldName</c> to <c>newName</c>.</summary>
[PipeNode(TestNodeIds.Versioned, "Versioned", Version = 2)]
public sealed class VersionedNode : PipeItemNodeBase<VersionedParameters>, IPipeNodeMigration
{
    public JsonObject MigrateParameters(int fromVersion, JsonObject parameters)
    {
        if (fromVersion < 2 && parameters["oldName"] is { } value)
        {
            parameters.Remove("oldName");
            parameters["newName"] = value;
        }
        return parameters;
    }

    protected override Task<PipeItem?> ProcessItemAsync(PipeItem item, VersionedParameters parameters, IPipeNodeContext context, CancellationToken cancellationToken)
    {
        item.Json["name"] = parameters.NewName;
        return Task.FromResult<PipeItem?>(item);
    }
}
