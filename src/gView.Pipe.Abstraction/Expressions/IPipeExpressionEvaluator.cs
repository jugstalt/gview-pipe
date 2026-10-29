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

public sealed class PipeExpressionScope
{
    /// <summary>JSON of the current item (<c>$json</c>).</summary>
    public JsonObject? Json { get; init; }

    public int ItemIndex { get; init; }

    public string WorkflowId { get; init; } = "";

    public string WorkflowName { get; init; } = "";

    public string ExecutionId { get; init; } = "";

    /// <summary>Resolves <c>$node["Name"].json</c>: the first main output item of an executed node.</summary>
    public Func<string, JsonObject?>? NodeJson { get; init; }
}

public class PipeExpressionException : PipeException
{
    public PipeExpressionException(string message) : base(message) { }
}
