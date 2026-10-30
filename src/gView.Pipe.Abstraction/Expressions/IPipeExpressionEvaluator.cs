using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Expressions;

/// <summary>
/// Evaluates <c>{{ }}</c> templates in parameter values. A template that consists of a single expression
/// returns the typed value (number, object, …); mixed text returns a string.
/// </summary>
public interface IPipeExpressionEvaluator
{
    bool ContainsExpression(string text);

    JsonNode? Evaluate(string template, PipeExpressionScope scope);
}

/// <summary>
/// Runs a script (JavaScript function body; <c>return</c> gives the result) in a sandbox
/// with the same variables as expressions. Used by nodes such as Code.
/// </summary>
public interface IPipeScriptRunner
{
    /// <param name="variables">Additional global variables, e.g. <c>items</c>.</param>
    /// <param name="log">Receives <c>console.log</c> output.</param>
    JsonNode? Run(
        string code,
        PipeExpressionScope scope,
        IReadOnlyDictionary<string, JsonNode?>? variables = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What expressions and scripts can see.</summary>
public sealed class PipeExpressionScope
{
    /// <summary>JSON of the current item (<c>$json</c>).</summary>
    public JsonObject? Json { get; init; }

    public int ItemIndex { get; init; }

    public string WorkflowId { get; init; } = "";

    public string WorkflowName { get; init; } = "";

    public string ExecutionId { get; init; } = "";

    /// <summary>Main input items of the current node (<c>$input.all()</c>, <c>$input.first()</c>).</summary>
    public IReadOnlyList<JsonObject>? Input { get; init; }

    /// <summary>
    /// Main output items of an executed node by name (<c>$node["Name"].json</c> is the first, <c>.all</c> all of them).
    /// Returns null for unknown or not yet executed nodes.
    /// </summary>
    public Func<string, IReadOnlyList<JsonObject>?>? NodeItems { get; init; }
}

public class PipeExpressionException : PipeException
{
    public PipeExpressionException(string message) : base(message) { }
    public PipeExpressionException(string message, Exception innerException) : base(message, innerException) { }
}
