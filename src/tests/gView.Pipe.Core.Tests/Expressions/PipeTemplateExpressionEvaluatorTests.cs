using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Core.Expressions;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Expressions;

public class PipeTemplateExpressionEvaluatorTests
{
    private readonly PipeTemplateExpressionEvaluator _evaluator = new();

    private readonly PipeExpressionScope _scope = new()
    {
        Json = JsonNode.Parse("""
            {
              "name": "gView",
              "count": 3,
              "address": { "city": "Graz" },
              "tags": ["a", "b"],
              "with space": true
            }
            """)!.AsObject(),
        ItemIndex = 7,
        WorkflowId = "wf-1",
        WorkflowName = "My Flow",
        ExecutionId = "ex-1",
        NodeJson = name => name == "Http" ? new JsonObject { ["id"] = 42 } : null
    };

    [Theory]
    [InlineData("{{ $json.name }}", "gView")]
    [InlineData("{{$json.address.city}}", "Graz")]
    [InlineData("{{ $json.tags[1] }}", "b")]
    [InlineData("{{ $json[\"with space\"] }}", "true")]
    [InlineData("{{ $json['address']['city'] }}", "Graz")]
    [InlineData("{{ $workflow.name }}", "My Flow")]
    [InlineData("{{ $execution.id }}", "ex-1")]
    [InlineData("Hello {{ $json.name }} from {{ $json.address.city }}!", "Hello gView from Graz!")]
    [InlineData("no expression", "no expression")]
    public void Evaluates_paths_and_templates(string template, string expected)
    {
        var result = _evaluator.Evaluate(template, _scope);

        Assert.Equal(expected, result is JsonValue v && v.TryGetValue<string>(out var s) ? s : result?.ToJsonString());
    }

    [Fact]
    public void Single_expression_returns_typed_values()
    {
        Assert.Equal(3, _evaluator.Evaluate("{{ $json.count }}", _scope)!.GetValue<int>());
        Assert.Equal(7, _evaluator.Evaluate("{{ $itemIndex }}", _scope)!.GetValue<int>());
        Assert.IsType<JsonObject>(_evaluator.Evaluate("{{ $json.address }}", _scope));
        Assert.Equal(42, _evaluator.Evaluate("{{ $node[\"Http\"].json.id }}", _scope)!.GetValue<int>());
    }

    [Fact]
    public void Embedded_objects_are_rendered_as_json()
    {
        Assert.Equal("tags: [\"a\",\"b\"]", _evaluator.Evaluate("tags: {{ $json.tags }}", _scope)!.GetValue<string>());
    }

    [Fact]
    public void Missing_values_are_null_or_empty()
    {
        Assert.Null(_evaluator.Evaluate("{{ $json.missing.deeper }}", _scope));
        Assert.Null(_evaluator.Evaluate("{{ $json.tags[9] }}", _scope));
        Assert.Null(_evaluator.Evaluate("{{ $node[\"Nope\"].json.id }}", _scope));
        Assert.Equal("x=", _evaluator.Evaluate("x={{ $json.missing }}", _scope)!.GetValue<string>());
    }

    [Fact]
    public void Result_is_a_copy_of_the_scope_data()
    {
        var address = _evaluator.Evaluate("{{ $json.address }}", _scope)!.AsObject();
        address["city"] = "Wien";

        Assert.Equal("Graz", _scope.Json!["address"]!["city"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{{ $unknown }}")]
    [InlineData("{{ $json.name")]
    [InlineData("{{ $json.name + 1 }}")]
    [InlineData("{{ $node.json }}")]
    public void Invalid_expressions_throw(string template)
    {
        Assert.Throws<PipeExpressionException>(() => _evaluator.Evaluate(template, _scope));
    }
}
