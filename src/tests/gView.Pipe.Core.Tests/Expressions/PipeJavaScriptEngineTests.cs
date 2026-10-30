using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Core.Expressions;
using gView.Pipe.Core.Options;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Expressions;

public class PipeJavaScriptEngineTests
{
    private readonly PipeJavaScriptEngine _engine = Create();

    private readonly PipeExpressionScope _scope = new()
    {
        Json = JsonNode.Parse("""
            {
              "name": "gView",
              "count": 3,
              "price": 10.5,
              "address": { "city": "Graz" },
              "tags": ["a", "b"],
              "with space": true
            }
            """)!.AsObject(),
        ItemIndex = 7,
        WorkflowId = "wf-1",
        WorkflowName = "My Flow",
        ExecutionId = "ex-1",
        Input = [new JsonObject { ["n"] = 1 }, new JsonObject { ["n"] = 2 }],
        NodeItems = name => name == "Http" ? [new JsonObject { ["id"] = 42 }, new JsonObject { ["id"] = 43 }] : null
    };

    static private PipeJavaScriptEngine Create(Action<PipeScriptOptions>? configure = null)
    {
        var options = new PipeCoreOptions();
        configure?.Invoke(options.Scripts);
        return new PipeJavaScriptEngine(Microsoft.Extensions.Options.Options.Create(options));
    }

    private string Text(string template)
        => _engine.Evaluate(template, _scope) switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            var node => node?.ToJsonString() ?? "null"
        };

    [Theory]
    // the path syntax of the first version is plain JavaScript and keeps working
    [InlineData("{{ $json.name }}", "gView")]
    [InlineData("{{$json.address.city}}", "Graz")]
    [InlineData("{{ $json.tags[1] }}", "b")]
    [InlineData("{{ $json[\"with space\"] }}", "true")]
    [InlineData("{{ $json['address']['city'] }}", "Graz")]
    [InlineData("{{ $workflow.name }}", "My Flow")]
    [InlineData("{{ $execution.id }}", "ex-1")]
    [InlineData("Hello {{ $json.name }} from {{ $json.address.city }}!", "Hello gView from Graz!")]
    [InlineData("no expression", "no expression")]
    // JavaScript
    [InlineData("{{ $json.price * 2 }}", "21")]
    [InlineData("{{ $json.name.toUpperCase() }}", "GVIEW")]
    [InlineData("{{ $json.count > 2 ? 'many' : 'few' }}", "many")]
    [InlineData("{{ $json.tags.join(', ') }}", "a, b")]
    [InlineData("{{ `${$json.name}-${$itemIndex}` }}", "gView-7")]
    [InlineData("{{ $input.all().length }}", "2")]
    [InlineData("{{ $input.first().n }}", "1")]
    [InlineData("{{ $node[\"Http\"].all.map(i => i.id).join('+') }}", "42+43")]
    [InlineData("{{ $json.missing?.deeper ?? 'default' }}", "default")]
    public void Evaluates_expressions_and_templates(string template, string expected)
    {
        Assert.Equal(expected, Text(template));
    }

    [Fact]
    public void Single_expression_returns_typed_values()
    {
        Assert.Equal(3, _engine.Evaluate("{{ $json.count }}", _scope)!.GetValue<int>());
        Assert.Equal(7, _engine.Evaluate("{{ $itemIndex }}", _scope)!.GetValue<int>());
        Assert.True(_engine.Evaluate("{{ $json.count === 3 }}", _scope)!.GetValue<bool>());
        Assert.IsType<JsonObject>(_engine.Evaluate("{{ $json.address }}", _scope));
        Assert.Equal(42, _engine.Evaluate("{{ $node[\"Http\"].json.id }}", _scope)!.GetValue<int>());
        Assert.Equal("""{"a":1}""", _engine.Evaluate("{{ { a: 1 } }}", _scope)!.ToJsonString());
    }

    [Fact]
    public void Javascript_values_are_converted_to_json()
    {
        Assert.Null(_engine.Evaluate("{{ undefined }}", _scope));
        Assert.Equal("2026-01-02T03:04:05.000Z", _engine.Evaluate("{{ new Date(Date.UTC(2026, 0, 2, 3, 4, 5)) }}", _scope)!.GetValue<string>());
        Assert.Equal("tags: [\"a\",\"b\"]", Text("tags: {{ $json.tags }}"));
    }

    [Fact]
    public void Missing_values_are_null_or_empty()
    {
        Assert.Null(_engine.Evaluate("{{ $json.missing }}", _scope));
        Assert.Null(_engine.Evaluate("{{ $json.tags[9] }}", _scope));
        Assert.Null(_engine.Evaluate("{{ $node[\"Nope\"] }}", _scope));
        Assert.Equal("x=", Text("x={{ $json.missing }}"));
    }

    [Fact]
    public void Changes_inside_an_expression_do_not_leak_into_the_scope()
    {
        _engine.Evaluate("{{ ($json.address.city = 'Wien', 1) }}", _scope);

        Assert.Equal("Graz", _scope.Json!["address"]!["city"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{{ $unknown }}", "not defined")]
    [InlineData("{{ $json.name", "Unclosed")]
    [InlineData("{{ $json. }}", "Syntax error")]
    [InlineData("{{ $json.missing.deeper }}", "Cannot read")]
    [InlineData("{{ () => 1 }}", "function")]
    [InlineData("{{ (() => { const a = {}; a.self = a; return a; })() }}", "cyclic")]
    [InlineData("{{ (() => { while (true) {} })() }}", "")]
    [InlineData("{{ (function f() { return f(); })() }}", "")]
    public void Invalid_or_runaway_expressions_throw(string template, string messagePart)
    {
        var ex = Assert.Throws<PipeExpressionException>(() => _engine.Evaluate(template, _scope));

        Assert.Contains(messagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText('x')")]
    [InlineData("importNamespace('System.IO')")]
    [InlineData("require('fs')")]
    [InlineData("$env")]
    public void Sandbox_has_no_access_to_the_host(string expression)
    {
        Assert.Throws<PipeExpressionException>(() => _engine.Evaluate($"{{{{ {expression} }}}}", _scope));
    }

    [Fact]
    public void Timeout_stops_long_running_expressions()
    {
        var engine = Create(o =>
        {
            o.ExpressionTimeout = TimeSpan.FromMilliseconds(200);
            o.ExpressionMaxStatements = 0;
        });

        var started = DateTime.UtcNow;
        Assert.Throws<PipeExpressionException>(() => engine.Evaluate("{{ (() => { while (true) {} })() }}", _scope));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Scripts_return_values_and_log()
    {
        var log = new List<string>();
        var result = _engine.Run(
            """
            console.log('items', items.length, { a: 1 });
            return items.map(i => ({ n: i.n * 10, flow: $workflow.name }));
            """,
            _scope,
            new Dictionary<string, JsonNode?> { ["items"] = new JsonArray(new JsonObject { ["n"] = 1 }, new JsonObject { ["n"] = 2 }) },
            log.Add);

        Assert.Equal("""[{"n":10,"flow":"My Flow"},{"n":20,"flow":"My Flow"}]""", result!.ToJsonString());
        Assert.Equal(["items 2 {\"a\":1}"], log);
    }

    [Fact]
    public void Script_errors_report_the_line_of_the_user_code()
    {
        var ex = Assert.Throws<PipeExpressionException>(() => _engine.Run("const a = 1;\nnull.x;", _scope));

        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void Scripts_can_be_canceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Assert.ThrowsAny<OperationCanceledException>(() => _engine.Run("while (true) {}", _scope, cancellationToken: cts.Token));
    }
}
