using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Workflows;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Engine;

public class PipeEngineTests
{
    static private readonly object TwoItems = new { items = new[] { new { n = 1 }, new { n = 2 } } };

    static private async Task<PipeExecution> RunAsync(PipeWorkflow workflow, PipeExecutionRequest? request = null, CancellationToken cancellationToken = default)
    {
        using var services = PipeTestHost.Create();
        return await services.Engine().ExecuteAsync(workflow, request, cancellationToken);
    }

    static private List<JsonObject> Output(PipeExecution execution, string nodeId, string port = PipePorts.Main)
        => execution.NodeRuns[nodeId].Outputs!.TryGetValue(port, out var items) ? items.Select(i => i.Json).ToList() : [];

    [Fact]
    public async Task Linear_workflow_passes_items_and_evaluates_expressions_per_item()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("tag", TestNodeIds.Tag, new { field = "double", value = "n={{ $json.n }}" })
            .Connect("emit", "tag")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(["n=1", "n=2"], Output(execution, "tag").Select(j => j["double"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Single_expression_keeps_the_json_type()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("tag", TestNodeIds.Tag, new { field = "copy", value = "{{ $json.n }}" })
            .Connect("emit", "tag")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(2, Output(execution, "tag")[1]["copy"]!.GetValue<int>());
    }

    [Fact]
    public async Task Node_expression_reads_output_of_an_earlier_node()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, new { items = new[] { new { id = "abc" } } })
            .Node("other", TestNodeIds.Emit, new { items = new[] { new { x = 1 } } })
            .Node("tag", TestNodeIds.Tag, new { field = "from", value = "{{ $node[\"emit\"].json.id }}" })
            .Connect("emit", "other")
            .Connect("other", "tag")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal("abc", Output(execution, "tag")[0]["from"]!.GetValue<string>());
    }

    [Fact]
    public async Task Node_without_input_items_is_skipped_unless_always_execute()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, new { items = Array.Empty<object>() })
            .Node("skipped", TestNodeIds.Echo)
            .Node("forced", TestNodeIds.Echo, configure: n => n.Settings.AlwaysExecute = true)
            .Node("after-skipped", TestNodeIds.Echo)
            .Connect("emit", "skipped")
            .Connect("emit", "forced")
            .Connect("skipped", "after-skipped")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(PipeNodeRunStatus.Skipped, execution.NodeRuns["skipped"].Status);
        Assert.Equal(PipeNodeRunStatus.Skipped, execution.NodeRuns["after-skipped"].Status);
        Assert.Equal(PipeNodeRunStatus.Success, execution.NodeRuns["forced"].Status);
    }

    [Fact]
    public async Task Node_with_multiple_inputs_waits_for_all_branches()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("fast", TestNodeIds.Tag, new { field = "branch", value = "fast" })
            .Node("slow", TestNodeIds.Slow, new { delayMs = 200 })
            .Node("join", TestNodeIds.Echo)
            .Connect("emit", "fast")
            .Connect("emit", "slow")
            .Connect("fast", "join")
            .Connect("slow", "join", targetPort: "second")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(4, Output(execution, "join").Count);
        Assert.Equal(2, Output(execution, "join").Count(j => j["branch"] is null));
    }

    [Fact]
    public async Task Branches_share_no_item_instances()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("a", TestNodeIds.Tag, new { field = "branch", value = "a" })
            .Node("b", TestNodeIds.Echo)
            .Connect("emit", "a")
            .Connect("emit", "b")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.All(Output(execution, "b"), j => Assert.Null(j["branch"]));
        Assert.All(Output(execution, "emit"), j => Assert.Null(j["branch"]));
    }

    [Fact]
    public async Task Independent_branches_run_in_parallel()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("slow1", TestNodeIds.Slow, new { delayMs = 500 })
            .Node("slow2", TestNodeIds.Slow, new { delayMs = 500 })
            .Connect("emit", "slow1")
            .Connect("emit", "slow2")
            .Build();

        var stopwatch = Stopwatch.StartNew();
        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.True(stopwatch.ElapsedMilliseconds < 950, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Retries_until_the_node_succeeds()
    {
        var key = Guid.NewGuid().ToString();
        var workflow = new PipeWorkflowBuilder()
            .Node("fail", TestNodeIds.Fail, new { key, failTimes = 2 }, n =>
            {
                n.Settings.RetryCount = 2;
                n.Settings.RetryDelayMs = 0;
            })
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(3, execution.NodeRuns["fail"].Attempts);
    }

    [Fact]
    public async Task Error_mode_stop_fails_the_execution()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString() })
            .Node("after", TestNodeIds.Echo)
            .Connect("fail", "after")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Equal("fail", execution.ErrorNodeId);
        Assert.Contains("boom", execution.Error);
        Assert.Equal(PipeNodeRunStatus.Error, execution.NodeRuns["fail"].Status);
        Assert.False(execution.NodeRuns.ContainsKey("after"));
    }

    [Fact]
    public async Task Error_mode_continue_passes_input_items_on()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString() }, n => n.Settings.OnError = PipeNodeErrorMode.Continue)
            .Node("after", TestNodeIds.Echo)
            .Connect("emit", "fail")
            .Connect("fail", "after")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(PipeNodeRunStatus.Error, execution.NodeRuns["fail"].Status);
        Assert.Equal(2, Output(execution, "after").Count);
    }

    [Fact]
    public async Task Error_mode_error_output_routes_items_to_the_error_port()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString() }, n => n.Settings.OnError = PipeNodeErrorMode.ErrorOutput)
            .Node("ok", TestNodeIds.Echo)
            .Node("errors", TestNodeIds.Echo)
            .Connect("emit", "fail")
            .Connect("fail", "ok")
            .Connect("fail", "errors", sourcePort: PipePorts.Error)
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(PipeNodeRunStatus.Skipped, execution.NodeRuns["ok"].Status);
        var errors = Output(execution, "errors");
        Assert.Equal(2, errors.Count);
        Assert.Contains("boom", errors[0]["error"]!["message"]!.GetValue<string>());
        Assert.Equal(1, errors[0]["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task Timeout_fails_the_node()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 5000 }, n => n.Settings.TimeoutSeconds = 1)
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("Timed out", execution.Error);
    }

    [Fact]
    public async Task Cancellation_cancels_the_execution()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("slow", TestNodeIds.Slow, new { delayMs = 5000 })
            .Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var execution = await RunAsync(workflow, cancellationToken: cts.Token);

        Assert.Equal(PipeExecutionStatus.Canceled, execution.Status);
        Assert.Equal(PipeNodeRunStatus.Canceled, execution.NodeRuns["slow"].Status);
    }

    [Fact]
    public async Task Cycles_are_rejected()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("start", TestNodeIds.Echo)
            .Node("a", TestNodeIds.Echo)
            .Node("b", TestNodeIds.Echo)
            .Connect("start", "a")
            .Connect("a", "b")
            .Connect("b", "a")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("cycle", execution.Error);
    }

    [Fact]
    public async Task Unknown_node_type_fails_with_a_clear_message()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("missing", TestNodeIds.Unknown)
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Error, execution.Status);
        Assert.Contains("Unknown node type", execution.Error);
    }

    [Fact]
    public async Task Disabled_node_passes_items_through()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString() }, n => n.Disabled = true)
            .Node("after", TestNodeIds.Echo)
            .Connect("emit", "fail")
            .Connect("fail", "after")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(PipeNodeRunStatus.Disabled, execution.NodeRuns["fail"].Status);
        Assert.Equal(2, Output(execution, "after").Count);
    }

    [Fact]
    public async Task Start_node_limits_the_execution_to_its_reachable_nodes()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("trigger-a", TestNodeIds.Trigger)
            .Node("trigger-b", TestNodeIds.Trigger)
            .Node("join", TestNodeIds.Echo)
            .Connect("trigger-a", "join")
            .Connect("trigger-b", "join", targetPort: "second")
            .Build();

        var execution = await RunAsync(workflow, new PipeExecutionRequest
        {
            StartNodeId = "trigger-a",
            InputItems = [new(new JsonObject { ["from"] = "a" })]
        });

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.False(execution.NodeRuns.ContainsKey("trigger-b"));
        Assert.Equal("a", Assert.Single(Output(execution, "join"))["from"]!.GetValue<string>());
    }

    [Fact]
    public async Task Older_type_version_is_migrated_before_execution()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("versioned", TestNodeIds.Versioned, new { oldName = "migrated" }, n => n.TypeVersion = 1)
            .Connect("emit", "versioned")
            .Build();

        var execution = await RunAsync(workflow);

        Assert.Equal("migrated", Output(execution, "versioned")[0]["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Node_runs_record_a_snapshot_of_their_inputs()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("tag", TestNodeIds.Tag, new { field = "tag", value = "x" })
            .Connect("emit", "tag")
            .Build();

        var execution = await RunAsync(workflow);
        var run = execution.NodeRuns["tag"];

        Assert.Equal(2, run.InputCounts[PipePorts.Main]);
        // the Tag node changes the items it received; the recorded input must not change with them
        Assert.All(run.Inputs![PipePorts.Main], i => Assert.Null(i.Json["tag"]));
        Assert.All(Output(execution, "tag"), j => Assert.Equal("x", j["tag"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Retries_and_failures_are_written_to_the_node_log()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("fail", TestNodeIds.Fail, new { key = Guid.NewGuid().ToString() }, n =>
            {
                n.Settings.RetryCount = 1;
                n.Settings.RetryDelayMs = 0;
            })
            .Build();

        var execution = await RunAsync(workflow);
        var logs = execution.NodeRuns["fail"].Logs;

        Assert.Collection(logs,
            entry =>
            {
                Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
                Assert.Contains("attempt 1 failed", entry.Message);
            },
            entry =>
            {
                Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
                Assert.Contains("boom 2", entry.Message);
            });
    }

    [Fact]
    public async Task Binary_content_of_the_request_is_available_in_the_execution()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("start", TestNodeIds.Trigger)
            .Node("read", TestNodeIds.BinaryLength)
            .Connect("start", "read")
            .Build();

        var item = new PipeItem();
        item.Binary["file"] = new PipeBinaryRef("upload-1", "a.txt", "text/plain", 5);

        var execution = await RunAsync(workflow, new PipeExecutionRequest
        {
            InputItems = [item],
            BinaryContent = new Dictionary<string, byte[]> { ["upload-1"] = "hello"u8.ToArray() }
        });

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(5, Output(execution, "read")[0]["fileLength"]!.GetValue<long>());
    }

    [Fact]
    public async Task Observer_receives_all_events()
    {
        var observer = new RecordingObserver();
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("echo", TestNodeIds.Echo)
            .Connect("emit", "echo")
            .Build();

        await RunAsync(workflow, new PipeExecutionRequest { Observer = observer });

        Assert.Equal(["execution-started", "node-started:emit", "node-finished:emit", "node-started:echo", "node-finished:echo", "execution-finished"], observer.Events);
    }

    static private PipeWorkflow PinnedWorkflow()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("tag", TestNodeIds.Tag, new { field = "seen", value = "{{ $json.n }}" })
            .Connect("emit", "tag")
            .Build();
        workflow.PinData = new()
        {
            ["emit"] = new() { [PipePorts.Main] = [new PipeItem(new JsonObject { ["n"] = 42 })] }
        };
        return workflow;
    }

    [Fact]
    public async Task Manual_run_uses_pin_data_instead_of_executing_the_node()
    {
        var execution = await RunAsync(PinnedWorkflow());

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.True(execution.NodeRuns["emit"].Pinned);
        Assert.Equal([42], Output(execution, "tag").Select(j => j["seen"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Trigger_run_ignores_pin_data()
    {
        var execution = await RunAsync(PinnedWorkflow(), new PipeExecutionRequest { Mode = PipeExecutionMode.Trigger });

        Assert.False(execution.NodeRuns["emit"].Pinned);
        Assert.Equal([1, 2], Output(execution, "tag").Select(j => j["seen"]!.GetValue<int>()));
    }

    [Fact]
    public async Task Run_to_here_executes_only_the_target_and_its_predecessors()
    {
        var workflow = new PipeWorkflowBuilder()
            .Node("emit", TestNodeIds.Emit, TwoItems)
            .Node("a", TestNodeIds.Echo)
            .Node("b", TestNodeIds.Echo)
            .Node("after-a", TestNodeIds.Echo)
            .Node("other-root", TestNodeIds.Emit, TwoItems)
            .Connect("emit", "a")
            .Connect("emit", "b")
            .Connect("a", "after-a")
            .Build();

        var execution = await RunAsync(workflow, new PipeExecutionRequest { TargetNodeId = "a" });

        Assert.Equal(PipeExecutionStatus.Success, execution.Status);
        Assert.Equal(["a", "emit"], execution.NodeRuns.Keys.Order());
        Assert.Equal(2, Output(execution, "a").Count);
    }

    private sealed class RecordingObserver : IPipeExecutionObserver
    {
        public List<string> Events { get; } = new();

        public Task OnExecutionStartedAsync(PipeExecution execution) => Record("execution-started");
        public Task OnNodeStartedAsync(PipeExecution execution, PipeNodeRun run) => Record($"node-started:{run.NodeId}");
        public Task OnNodeFinishedAsync(PipeExecution execution, PipeNodeRun run) => Record($"node-finished:{run.NodeId}");
        public Task OnExecutionFinishedAsync(PipeExecution execution) => Record("execution-finished");

        private Task Record(string text)
        {
            lock (Events)
            {
                Events.Add(text);
            }
            return Task.CompletedTask;
        }
    }
}
