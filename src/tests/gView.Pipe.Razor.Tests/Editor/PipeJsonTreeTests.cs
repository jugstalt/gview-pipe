using gView.Pipe.Razor.Editor;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Tests.Editor;

public class PipeJsonTreeTests : BunitContext
{
    [Theory]
    [InlineData("name", "$json.name")]
    [InlineData("$id", "$json.$id")]
    [InlineData("first name", "$json[\"first name\"]")]
    [InlineData("1st", "$json[\"1st\"]")]
    public void Member_uses_dot_access_only_for_identifiers(string key, string expected)
        => Assert.Equal(expected, PipeJsonTree.Member("$json", key));

    [Fact]
    public void Clicking_a_nested_property_reports_its_path()
    {
        string? selected = null;
        var json = new JsonObject
        {
            ["customer"] = new JsonObject { ["first name"] = "Ada" },
            ["tags"] = new JsonArray(new JsonObject { ["id"] = 7 })
        };

        var tree = Render<PipeJsonTree>(ps => ps
            .Add(p => p.Value, json)
            .Add(p => p.OnSelectPath, path => selected = path));

        var keys = tree.FindAll(".gview-pipe-json-key");
        Assert.Equal(["\"customer\"", "\"first name\"", "\"tags\"", "\"id\""], keys.Select(k => k.TextContent));

        keys[1].Click();
        Assert.Equal("$json.customer[\"first name\"]", selected);

        // the click re-rendered the tree; find the elements again
        tree.FindAll(".gview-pipe-json-key")[3].Click();
        Assert.Equal("$json.tags[0].id", selected);
    }
}
