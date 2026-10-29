using gView.Pipe.Abstraction.Expressions;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Expressions;

/// <summary>
/// Simple path-based templating:
/// <c>{{ $json.a.b[0] }}</c>, <c>{{ $json["with space"] }}</c>, <c>{{ $node["Http"].json.id }}</c>,
/// <c>{{ $itemIndex }}</c>, <c>{{ $now }}</c>, <c>{{ $workflow.name }}</c>, <c>{{ $execution.id }}</c>.
/// No operators or functions; a script based evaluator can replace this behind <see cref="IPipeExpressionEvaluator"/>.
/// </summary>
public sealed class PipeTemplateExpressionEvaluator : IPipeExpressionEvaluator
{
    private const string Open = "{{";
    private const string Close = "}}";

    public bool ContainsExpression(string text) => text.Contains(Open, StringComparison.Ordinal);

    public JsonNode? Evaluate(string template, PipeExpressionScope scope)
    {
        var segments = Parse(template);

        if (segments.Count == 1 && segments[0].IsExpression)
        {
            return EvaluateExpression(segments[0].Text, scope)?.DeepClone();
        }

        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder.Append(segment.IsExpression
                ? ToText(EvaluateExpression(segment.Text, scope))
                : segment.Text);
        }

        return JsonValue.Create(builder.ToString());
    }

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
                throw new PipeExpressionException($"Unclosed expression in '{template}'");
            }

            segments.Add((true, template[(start + Open.Length)..end].Trim()));
            position = end + Close.Length;
        }

        return segments;
    }

    static private string ToText(JsonNode? value)
        => value switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => value.ToJsonString()
        };

    static private JsonNode? EvaluateExpression(string expression, PipeExpressionScope scope)
    {
        var reader = new PathReader(expression);
        var root = reader.ReadIdentifier();

        JsonNode? current = root switch
        {
            "$json" => scope.Json,
            "$itemIndex" => JsonValue.Create(scope.ItemIndex),
            "$now" => JsonValue.Create(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            "$workflow" => new JsonObject { ["id"] = scope.WorkflowId, ["name"] = scope.WorkflowName },
            "$execution" => new JsonObject { ["id"] = scope.ExecutionId },
            "$node" => ReadNode(reader, scope),
            _ => throw new PipeExpressionException($"Unknown expression root '{root}' in '{expression}'")
        };

        while (!reader.End)
        {
            if (reader.TryRead('.'))
            {
                // read first: the identifier must be consumed even if current is null
                var property = reader.ReadIdentifier();
                current = (current as JsonObject)?[property];
            }
            else if (reader.Peek == '[')
            {
                var index = reader.ReadIndex();
                current = index switch
                {
                    int i => current is JsonArray array && i >= 0 && i < array.Count ? array[i] : null,
                    string key => (current as JsonObject)?[key],
                    _ => null
                };
            }
            else
            {
                throw new PipeExpressionException($"Unexpected '{reader.Peek}' in '{expression}'");
            }
        }

        return current;
    }

    static private JsonNode? ReadNode(PathReader reader, PipeExpressionScope scope)
    {
        if (reader.ReadIndex() is not string nodeName)
        {
            throw new PipeExpressionException("Expected $node[\"Node name\"]");
        }

        if (!reader.TryRead('.') || reader.ReadIdentifier() != "json")
        {
            throw new PipeExpressionException($"Expected $node[\"{nodeName}\"].json");
        }

        return scope.NodeJson?.Invoke(nodeName);
    }

    private sealed class PathReader
    {
        private readonly string _text;
        private int _position;

        public PathReader(string text)
        {
            _text = text;
        }

        public bool End
        {
            get
            {
                SkipWhitespace();
                return _position >= _text.Length;
            }
        }

        public char Peek
        {
            get
            {
                SkipWhitespace();
                return _position < _text.Length ? _text[_position] : '\0';
            }
        }

        public bool TryRead(char c)
        {
            if (Peek != c)
            {
                return false;
            }

            _position++;
            return true;
        }

        public string ReadIdentifier()
        {
            SkipWhitespace();
            var start = _position;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] is '_' or '$'))
            {
                _position++;
            }

            if (start == _position)
            {
                throw new PipeExpressionException($"Expected identifier at position {start} in '{_text}'");
            }

            return _text[start.._position];
        }

        /// <summary>Reads <c>[0]</c>, <c>["key"]</c> or <c>['key']</c>; returns int or string.</summary>
        public object ReadIndex()
        {
            if (!TryRead('['))
            {
                throw new PipeExpressionException($"Expected '[' in '{_text}'");
            }

            SkipWhitespace();
            object result;

            if (_position < _text.Length && _text[_position] is '"' or '\'')
            {
                var quote = _text[_position++];
                var end = _text.IndexOf(quote, _position);
                if (end < 0)
                {
                    throw new PipeExpressionException($"Unclosed string in '{_text}'");
                }

                result = _text[_position..end];
                _position = end + 1;
            }
            else
            {
                var start = _position;
                while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '-'))
                {
                    _position++;
                }

                if (!int.TryParse(_text[start.._position], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    throw new PipeExpressionException($"Expected index or quoted key in '{_text}'");
                }

                result = index;
            }

            if (!TryRead(']'))
            {
                throw new PipeExpressionException($"Expected ']' in '{_text}'");
            }

            return result;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }
    }
}
