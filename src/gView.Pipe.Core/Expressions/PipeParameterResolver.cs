using gView.Pipe.Abstraction.Expressions;
using gView.Pipe.Abstraction.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Expressions;

/// <summary>Evaluates all expression strings inside a parameter object and binds it to a parameter class.</summary>
static internal class PipeParameterResolver
{
    static public TParameters Resolve<TParameters>(JsonObject parameters, IPipeExpressionEvaluator evaluator, PipeExpressionScope scope)
        where TParameters : class, new()
    {
        var resolved = (JsonObject)parameters.DeepClone();
        ResolveInPlace(resolved, evaluator, scope);

        try
        {
            return resolved.Deserialize<TParameters>(PipeJson.Options) ?? new TParameters();
        }
        catch (JsonException ex)
        {
            throw new PipeExpressionException($"Invalid parameters for {typeof(TParameters).Name}: {ex.Message}");
        }
    }

    static private void ResolveInPlace(JsonNode? node, IPipeExpressionEvaluator evaluator, PipeExpressionScope scope)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsExpression(obj[key], evaluator, out var template))
                    {
                        obj[key] = evaluator.Evaluate(template, scope);
                    }
                    else
                    {
                        ResolveInPlace(obj[key], evaluator, scope);
                    }
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (IsExpression(array[i], evaluator, out var template))
                    {
                        array[i] = evaluator.Evaluate(template, scope);
                    }
                    else
                    {
                        ResolveInPlace(array[i], evaluator, scope);
                    }
                }
                break;
        }
    }

    static private bool IsExpression(JsonNode? node, IPipeExpressionEvaluator evaluator, out string template)
    {
        template = "";
        return node is JsonValue value
               && value.TryGetValue(out template!)
               && evaluator.ContainsExpression(template);
    }
}
