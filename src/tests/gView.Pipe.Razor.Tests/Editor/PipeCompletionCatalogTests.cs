using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Razor.Editor;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Tests.Editor;

public class PipeCompletionCatalogTests
{
    /// <summary>Start → Orders → Mail; the run shows the items that went through.</summary>
    static private (PipeWorkflow Workflow, PipeExecution Execution) Sample()
    {
        var workflow = new PipeWorkflow
        {
            Id = "wf",
            Nodes =
            [
                new PipeWorkflowNode { Id = "start", Name = "Start" },
                new PipeWorkflowNode { Id = "orders", Name = "Due Date" },
                new PipeWorkflowNode { Id = "mail", Name = "Mail" }
            ],
            Connections =
            [
                new PipeConnection { SourceNodeId = "start", TargetNodeId = "orders" },
                new PipeConnection { SourceNodeId = "orders", TargetNodeId = "mail" }
            ]
        };
        var order = new JsonObject
        {
            ["id"] = "A-1",
            ["amount"] = 120.5,
            ["customer"] = new JsonObject { ["name"] = "Ada" },
            ["lines"] = new JsonArray(new JsonObject { ["qty"] = 2 }),
            ["first name"] = "Ada"
        };
        var execution = new PipeExecution
        {
            NodeRuns =
            {
                ["orders"] = new PipeNodeRun { Outputs = new() { ["main"] = [new PipeItem(order)] } },
                ["mail"] = new PipeNodeRun { Inputs = new() { ["main"] = [new PipeItem((JsonObject)order.DeepClone())] } }
            }
        };
        return (workflow, execution);
    }

    [Fact]
    public void Input_fields_of_the_shown_run_become_paths_with_type_and_sample()
    {
        var (workflow, execution) = Sample();

        var set = PipeCompletionCatalog.Build(workflow, "mail", execution, null);
        var paths = set.Items.ToDictionary(i => i.Path);

        Assert.Null(set.Hint);
        Assert.Equal("number", paths["$json.amount"].Type);
        Assert.Equal("120.5", paths["$json.amount"].Sample);
        Assert.Equal("Ada", paths["$json.customer.name"].Sample);
        Assert.Equal("number", paths["$json.lines[0].qty"].Type);
        Assert.True(paths.ContainsKey("$json[\"first name\"]"));
        Assert.True(paths.ContainsKey("$node[\"Due Date\"].json.customer.name"));
        Assert.True(paths.ContainsKey("$node[\"Due Date\"].all"));
        Assert.True(paths.ContainsKey("$now"));
    }

    [Fact]
    public void Without_a_run_the_pinned_output_of_the_predecessor_is_used()
    {
        var (workflow, _) = Sample();
        workflow.PinData = new() { ["orders"] = new() { ["main"] = [new PipeItem(new JsonObject { ["pinned"] = true })] } };

        var set = PipeCompletionCatalog.Build(workflow, "mail", null, null);

        Assert.Contains(set.Items, i => i.Path == "$json.pinned" && i.Type == "boolean");
    }

    [Fact]
    public void Without_data_only_globals_and_a_hint()
    {
        var (workflow, _) = Sample();

        var set = PipeCompletionCatalog.Build(workflow, "mail", null, null);

        Assert.NotNull(set.Hint);
        Assert.DoesNotContain(set.Items, i => i.Path.StartsWith("$json.") || i.Path.StartsWith("$node["));
    }

    [Fact]
    public void Variables_are_listed_and_secret_values_never_leave_the_server()
    {
        var (workflow, _) = Sample();
        var variables = new PipeResolvedVariables
        {
            Values = new Dictionary<string, string> { ["API_URL"] = "https://api.example.com", ["API_KEY"] = "s3cret" },
            Secrets = new HashSet<string> { "API_KEY" }
        };

        var items = PipeCompletionCatalog.Build(workflow, "mail", null, variables).Items;

        Assert.Equal("https://api.example.com", items.Single(i => i.Path == "$vars.API_URL").Sample);
        var secret = items.Single(i => i.Path == "$vars.API_KEY");
        Assert.Null(secret.Sample);
        Assert.Equal("secret", secret.Detail);
    }

    [Fact]
    public void Large_and_deep_data_is_cut_off()
    {
        var deep = new JsonObject();
        var level = deep;
        for (var i = 0; i < 10; i++)
        {
            var next = new JsonObject();
            level["n"] = next;
            level = next;
        }
        var wide = new JsonObject();
        for (var i = 0; i < 1000; i++)
        {
            wide[$"f{i}"] = i;
        }

        var paths = PipeCompletionCatalog.Paths("$json", [deep, wide]).Select(p => p.Path).ToList();

        Assert.DoesNotContain("$json.n.n.n.n.n", paths);
        Assert.Contains("$json.n.n.n.n", paths);
        Assert.True(paths.Count <= PipeCompletionCatalog.MaxEntries);
    }
}
