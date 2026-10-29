using gView.Pipe.Plugins;
using gView.Pipe.Plugins.Nodes.Flow;
using gView.Pipe.Plugins.Nodes.Network;
using gView.Pipe.Plugins.Nodes.Transform;
using gView.Pipe.Razor.Forms;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Tests.Forms;

public class PipeParameterFieldsTests
{
    [Fact]
    public void Http_request_parameters_map_to_editors_in_declared_order()
    {
        var fields = PipeParameterFields.For(typeof(HttpRequestNodeParameters)).ToDictionary(f => f.Key);

        Assert.Equal(["method", "url", "query", "headers", "body", "contentType", "responseFormat", "failOnHttpError", "credentialId"],
            PipeParameterFields.For(typeof(HttpRequestNodeParameters)).Select(f => f.Key));

        Assert.Equal(PipeFieldKind.Text, fields["url"].Kind);
        Assert.True(fields["url"].Required);
        Assert.Equal(PipeFieldKind.Dictionary, fields["query"].Kind);
        Assert.Equal(PipeFieldKind.Text, fields["query"].ItemKind);
        Assert.Equal(PipeFieldKind.Json, fields["body"].Kind);
        Assert.True(fields["body"].Multiline);
        Assert.Equal(PipeFieldKind.Enum, fields["responseFormat"].Kind);
        Assert.Equal(["auto", "json", "text"], fields["responseFormat"].EnumValues);
        Assert.Equal(PipeFieldKind.Boolean, fields["failOnHttpError"].Kind);
        Assert.False(fields["failOnHttpError"].Expressions);
        Assert.Equal(PipeFieldKind.Credential, fields["credentialId"].Kind);
        Assert.Contains(Guid.Parse(PipeBuiltInCredentials.BearerToken), fields["credentialId"].CredentialTypeIds);
    }

    [Fact]
    public void Collections_of_json_values_get_loose_json_items()
    {
        var set = PipeParameterFields.For(typeof(SetNodeParameters)).ToDictionary(f => f.Key);
        var switchFields = PipeParameterFields.For(typeof(SwitchNodeParameters)).ToDictionary(f => f.Key);

        Assert.Equal((PipeFieldKind.Dictionary, PipeFieldKind.Json), (set["fields"].Kind, set["fields"].ItemKind));
        Assert.Equal((PipeFieldKind.Dictionary, PipeFieldKind.Text), (set["rename"].Kind, set["rename"].ItemKind));
        Assert.Equal((PipeFieldKind.List, PipeFieldKind.Json), (switchFields["cases"].Kind, switchFields["cases"].ItemKind));
    }

    [Fact]
    public void Unsupported_types_fall_back_to_raw_json()
    {
        var field = Assert.Single(PipeParameterFields.For(typeof(ComplexParameters)));

        Assert.Equal(PipeFieldKind.RawJson, field.Kind);
        Assert.Equal("Items list", field.Label);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("""{"a":1}""", """{"a":1}""")]
    [InlineData("hello", "\"hello\"")]
    [InlineData("{{ $json.a }}", "\"{{ $json.a }}\"")]
    [InlineData("{{ 1 }}", "\"{{ 1 }}\"")]
    public void Loose_text_becomes_typed_json_unless_it_is_an_expression(string text, string expectedJson)
    {
        Assert.Equal(expectedJson, PipeJsonText.FromLooseText(text)!.ToJsonString());
    }

    [Fact]
    public void Number_text_keeps_numbers_and_expressions()
    {
        Assert.Null(PipeJsonText.FromNumberText(" "));
        Assert.Equal(3, PipeJsonText.FromNumberText("3")!.GetValue<long>());
        Assert.Equal(3.5, PipeJsonText.FromNumberText("3.5")!.GetValue<double>());
        Assert.Equal("{{ $json.n }}", PipeJsonText.FromNumberText("{{ $json.n }}")!.GetValue<string>());
        Assert.Equal("text", PipeJsonText.ToText(JsonValue.Create("text")));
    }

    public sealed class ComplexParameters
    {
        public List<JsonObject> ItemsList { get; set; } = new();
    }
}
