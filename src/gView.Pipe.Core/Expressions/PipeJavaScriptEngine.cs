using Acornima.Ast;
using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Core.Options;
using Jint;
using JsEngine = Jint.Engine;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Json;
using Jint.Runtime;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Expressions;

/// <summary>
/// JavaScript (Jint) for <c>{{ }}</c> expressions and Code nodes.
/// Every evaluation gets a fresh, sandboxed engine: no CLR access, time/memory/recursion limits.
/// Data crosses the boundary as JSON only.
/// </summary>
public sealed class PipeJavaScriptEngine : IPipeExpressionEvaluator, IPipeScriptRunner
{
    private const string Open = "{{";
    private const string Close = "}}";
    private const int MaxCachedScripts = 2000;

    // $node and $input are lazy views on the data provided by the host functions __nodeItems and __input
    private const string PreludeSource = """
        var $node = new Proxy({}, {
            get: function (target, name) {
                if (typeof name !== 'string') return undefined;
                var items = __nodeItems(name);
                return items === undefined ? undefined : { json: items.length > 0 ? items[0] : null, all: items };
            }
        });
        // reading an undefined variable fails (in __var) instead of passing "undefined" on to a script or database
        var $vars = new Proxy({}, {
            get: function (target, name) {
                if (typeof name !== 'string' || name === 'toJSON' || name === 'then') return undefined;
                return __var(name);
            },
            has: function (target, name) { return typeof name === 'string' && __hasVar(name); },
            ownKeys: function () { return __varNames; },
            getOwnPropertyDescriptor: function (target, name) {
                return typeof name === 'string' && __hasVar(name) ? { value: __var(name), enumerable: true, configurable: true } : undefined;
            }
        });
        var $input = {
            all: function () { return __input; },
            first: function () { return __input.length > 0 ? __input[0] : undefined; }
        };
        var console = {
            log: function () {
                __log(Array.prototype.map.call(arguments, function (a) { return typeof a === 'string' ? a : JSON.stringify(a); }).join(' '));
            }
        };
        """;

    static private readonly Prepared<Script> Prelude = JsEngine.PrepareScript(PreludeSource, strict: true);

    private readonly PipeScriptOptions _options;
    private readonly ConcurrentDictionary<string, Prepared<Script>> _scripts = new();

    public PipeJavaScriptEngine(IOptions<PipeCoreOptions> options)
    {
        _options = options.Value.Scripts;
    }

    public bool ContainsExpression(string text) => text.Contains(Open, StringComparison.Ordinal);

    public JsonNode? Evaluate(string template, PipeExpressionScope scope)
    {
        var segments = Parse(template);
        if (!segments.Any(s => s.IsExpression))
        {
            return JsonValue.Create(template);
        }

        var engine = CreateEngine(scope, _options.ExpressionTimeout, _options.ExpressionMaxStatements, log: null, CancellationToken.None);

        if (segments.Count == 1)
        {
            return EvaluateExpression(engine, segments[0].Text);
        }

        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.IsExpression ? ToText(EvaluateExpression(engine, segment.Text)) : segment.Text);
        }
        return JsonValue.Create(builder.ToString());
    }

    public JsonNode? Run(
        string code,
        PipeExpressionScope scope,
        IReadOnlyDictionary<string, JsonNode?>? variables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var engine = CreateEngine(scope, _options.CodeTimeout, maxStatements: 0, log, cancellationToken);
        foreach (var (name, value) in variables ?? new Dictionary<string, JsonNode?>())
        {
            engine.SetValue(name, ToJs(engine, value));
        }

        // the code is a function body, so "return" works; the wrapper adds one line before the user code
        var script = Prepare($"(function () {{\n{code}\n}})()", "code");
        return Execute(engine, script, "code", lineOffset: -1, cancellationToken);
    }

    private JsonNode? EvaluateExpression(JsEngine engine, string expression)
    {
        // parentheses make object literals ({ a: 1 }) expressions instead of blocks
        var script = Prepare($"({expression}\n)", expression);
        return Execute(engine, script, expression, lineOffset: 0, CancellationToken.None);
    }

    private JsEngine CreateEngine(PipeExpressionScope scope, TimeSpan timeout, int maxStatements, Action<string>? log, CancellationToken cancellationToken)
    {
        var engine = new JsEngine(options =>
        {
            options.Strict();
            options.LimitRecursion(_options.MaxRecursionDepth);
            options.LimitMemory(_options.MemoryLimitBytes);
            options.TimeoutInterval(timeout);
            if (maxStatements > 0)
            {
                options.MaxStatements(maxStatements);
            }
            if (cancellationToken.CanBeCanceled)
            {
                options.CancellationToken(cancellationToken);
            }
        });

        engine.SetValue("$json", ToJs(engine, scope.Json));
        engine.SetValue("$itemIndex", scope.ItemIndex);
        engine.SetValue("$now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        engine.SetValue("$workflow", ToJs(engine, new JsonObject { ["id"] = scope.WorkflowId, ["name"] = scope.WorkflowName }));
        engine.SetValue("$execution", ToJs(engine, new JsonObject { ["id"] = scope.ExecutionId }));
        engine.SetValue("$environment", scope.Environment);
        var variables = scope.Variables ?? new Dictionary<string, string>();
        engine.SetValue("__var", new Func<string, string>(name =>
            variables.TryGetValue(name, out var value)
                ? value
                : throw new PipeExpressionException($"Variable '{name}' is not defined in environment {scope.Environment}")));
        engine.SetValue("__hasVar", new Func<string, bool>(variables.ContainsKey));
        engine.SetValue("__varNames", ToJs(engine, new JsonArray(variables.Keys.Order(StringComparer.Ordinal).Select(k => (JsonNode)JsonValue.Create(k)!).ToArray())));
        engine.SetValue("__input", ToJs(engine, ToArray(scope.Input ?? [])));
        engine.SetValue("__nodeItems", new Func<string, JsValue>(name =>
            scope.NodeItems?.Invoke(name) is { } items ? ToJs(engine, ToArray(items)) : JsValue.Undefined));
        engine.SetValue("__log", new Action<string>(log ?? (_ => { })));
        engine.Execute(Prelude);

        return engine;
    }

    private Prepared<Script> Prepare(string source, string label)
    {
        if (_scripts.TryGetValue(source, out var cached))
        {
            return cached;
        }

        try
        {
            var prepared = JsEngine.PrepareScript(source, strict: true);
            if (_scripts.Count >= MaxCachedScripts)
            {
                _scripts.Clear();
            }
            _scripts[source] = prepared;
            return prepared;
        }
        catch (Exception ex)
        {
            throw new PipeExpressionException($"Syntax error in '{Shorten(label)}': {ex.Message}", ex);
        }
    }

    static private JsonNode? Execute(JsEngine engine, Prepared<Script> script, string label, int lineOffset, CancellationToken cancellationToken)
    {
        try
        {
            return FromJs(engine, engine.Evaluate(script));
        }
        catch (PipeExpressionException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (JavaScriptException ex)
        {
            var line = ex.Location.Start.Line + lineOffset;
            var where = lineOffset != 0 && line > 0 ? $" (line {line})" : "";
            throw new PipeExpressionException($"{ex.Message}{where} in '{Shorten(label)}'", ex);
        }
        catch (Exception ex)
        {
            // timeout, memory, recursion and statement limits
            throw new PipeExpressionException($"{ex.Message} in '{Shorten(label)}'", ex);
        }
    }

    static private JsValue ToJs(JsEngine engine, JsonNode? value)
        => value is null ? JsValue.Null : new JsonParser(engine).Parse(value.ToJsonString());

    /// <summary>Back to JSON through JSON.stringify: undefined → null, Date → ISO text; functions and cycles fail.</summary>
    static private JsonNode? FromJs(JsEngine engine, JsValue value)
    {
        if (value.IsUndefined() || value.IsNull())
        {
            return null;
        }
        if (value is Function)
        {
            throw new PipeExpressionException("The result is a function, not a value");
        }

        var json = new JsonSerializer(engine).Serialize(value);
        return json.IsUndefined() ? null : JsonNode.Parse(json.AsString());
    }

    static private JsonArray ToArray(IEnumerable<JsonObject> items)
        => new(items.Select(i => (JsonNode)i.DeepClone()).ToArray());

    static private string ToText(JsonNode? value)
        => value switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => value.ToJsonString()
        };

    static private string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";

    static private List<(bool IsExpression, string Text)> Parse(string template)
    {
        var segments = new List<(bool, string)>();
        var position = 0;

        while (position < template.Length)
        {
            var start = template.IndexOf(Open, position, StringComparison.Ordinal);
            if (start < 0)
            {
                segments.Add((false, template[position..]));
                break;
            }

            if (start > position)
            {
                segments.Add((false, template[position..start]));
            }

            var end = template.IndexOf(Close, start + Open.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new PipeExpressionException($"Unclosed expression in '{Shorten(template)}'");
            }

            segments.Add((true, template[(start + Open.Length)..end].Trim()));
            position = end + Close.Length;
        }

        return segments;
    }
}
