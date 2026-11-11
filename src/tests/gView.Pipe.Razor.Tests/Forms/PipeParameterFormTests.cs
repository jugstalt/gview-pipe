using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Plugins;
using gView.Pipe.Plugins.Nodes.Network;
using gView.Pipe.Plugins.Nodes.Transform;
using gView.Pipe.Plugins.Nodes.Triggers;
using gView.Pipe.Razor.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using System.Text.Json.Nodes;

namespace gView.Pipe.Razor.Tests.Forms;

public class PipeParameterFormTests : BunitContext
{
    public PipeParameterFormTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
        });
    }

    /// <summary>MudSelect needs a popover provider; it registers through a service, so a separate render is enough.</summary>
    private IRenderedComponent<PipeParameterForm> RenderForm(Type parametersType, JsonObject parameters, Action? onChanged = null)
    {
        Render<MudPopoverProvider>();
        return Render<PipeParameterForm>(ps => ps
            .Add(p => p.ParametersType, parametersType)
            .Add(p => p.Parameters, parameters)
            .Add(p => p.OnChanged, EventCallback.Factory.Create(this, () => onChanged?.Invoke())));
    }

    [Fact]
    public void The_fields_of_a_form_trigger_are_edited_as_cards_and_keep_unknown_properties()
    {
        var parameters = new JsonObject
        {
            ["fields"] = new JsonArray(new JsonObject { ["name"] = "plate", ["type"] = "text", ["pattern"] = @"[A-Z]{2}-\d+", ["future"] = 1 })
        };
        var changes = 0;
        var form = RenderForm(typeof(FormTriggerNodeParameters), parameters, () => changes++);

        Assert.Equal("plate", form.Find("input[value=plate]").GetAttribute("value"));
        Assert.Contains("Pattern (regular expression)", form.Markup);

        form.FindAll("button").First(b => b.TextContent.Contains("Add field")).Click();

        var fields = (JsonArray)parameters["fields"]!;
        Assert.Equal(2, fields.Count);
        Assert.Equal("field1", (string?)fields[1]!["name"]);
        Assert.Equal(1, (int?)fields[0]!["future"]);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Editing_a_dictionary_value_writes_typed_json_into_the_parameters()
    {
        var parameters = new JsonObject { ["fields"] = new JsonObject { ["count"] = "1" } };
        var changes = 0;
        var form = RenderForm(typeof(SetNodeParameters), parameters, () => changes++);

        var valueInput = form.FindAll("input").Single(i => i.GetAttribute("value") == "1");
        valueInput.Input("7");

        form.WaitForAssertion(() => Assert.Equal(7, parameters["fields"]!["count"]!.GetValue<long>()), TimeSpan.FromSeconds(2));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Expressions_in_text_fields_stay_strings()
    {
        var parameters = new JsonObject { ["url"] = "https://old" };
        var form = RenderForm(typeof(HttpRequestNodeParameters), parameters);

        form.FindAll("input").Single(i => i.GetAttribute("value") == "https://old").Input("https://x/{{ $json.id }}");

        form.WaitForAssertion(() => Assert.Equal("https://x/{{ $json.id }}", parameters["url"]!.GetValue<string>()), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Credential_field_offers_only_matching_credential_types()
    {
        var store = Services.GetRequiredService<IPipeCredentialStore>();
        await store.SaveAsync(new PipeCredential { Id = "bearer", Name = "My token", TypeId = Guid.Parse(PipeBuiltInCredentials.BearerToken) });
        await store.SaveAsync(new PipeCredential { Id = "db", Name = "My database", TypeId = Guid.Parse(PipeBuiltInCredentials.ConnectionString) });

        var form = RenderForm(typeof(HttpRequestNodeParameters), new JsonObject());

        var select = form.FindComponents<MudSelect<string>>().Single(s => s.Instance.Label == "Credential");
        var items = select.FindComponents<MudSelectItem<string>>().Select(i => i.Instance.Value).ToList();

        Assert.Equal(["bearer"], items);
    }

    [Fact]
    public void Typing_into_a_text_field_applies_without_leaving_it()
    {
        var parameters = new JsonObject { ["url"] = "https://old" };
        var changes = 0;
        var form = RenderForm(typeof(HttpRequestNodeParameters), parameters, () => changes++);

        // input events only, no change event: the value must not wait for the field to lose the focus
        form.FindAll("input").Single(i => i.GetAttribute("value") == "https://old").Input("https://new");

        form.WaitForAssertion(() => Assert.Equal("https://new", parameters["url"]!.GetValue<string>()), TimeSpan.FromSeconds(2));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Code_fields_use_the_code_editor_with_their_language()
    {
        var form = RenderForm(typeof(CodeNodeParameters), new JsonObject { ["code"] = "return items;" });

        var field = form.FindComponent<PipeCodeField>();

        Assert.Equal("javascript", field.Instance.Language);
        Assert.Equal("return items;", field.Instance.Value);
        Assert.DoesNotContain(form.FindAll("textarea"), t => t.TextContent.Contains("return items;"));
    }

    [Fact]
    public void Expression_fields_show_the_preview_of_their_value()
    {
        Render<MudPopoverProvider>();
        var form = Render<PipeParameterForm>(ps => ps
            .Add(p => p.ParametersType, typeof(HttpRequestNodeParameters))
            .Add(p => p.Parameters, new JsonObject { ["url"] = "https://x/{{ $json.id }}", ["method"] = "GET" })
            .Add(p => p.ExpressionPreview, template => template.Replace("{{ $json.id }}", "7")));

        var preview = form.Find(".gview-pipe-expression-preview");

        Assert.Equal("= https://x/7", preview.TextContent.Trim());
    }

    [Fact]
    public void A_conditional_field_shows_only_for_its_values_and_keeps_its_value_while_hidden()
    {
        // the default format is binary: no CSV delimiter
        var parameters = new JsonObject { ["path"] = "orders/*.csv", ["csvDelimiter"] = ";" };
        var form = RenderForm(typeof(gView.Pipe.Plugins.Nodes.Files.ReadFileNodeParameters), parameters);
        Assert.DoesNotContain("CSV delimiter", form.Markup);

        parameters["format"] = "csv";
        form.Render();
        Assert.Contains("CSV delimiter", form.Markup);
        Assert.Equal(";", (string?)parameters["csvDelimiter"]);
    }

    [Fact]
    public void Advanced_fields_are_in_a_folded_section_and_choices_show_their_description()
    {
        var form = RenderForm(typeof(gView.Pipe.Plugins.Nodes.Files.ReadFileNodeParameters), new JsonObject());

        Assert.Contains("Advanced (2)", form.Markup);
        Assert.DoesNotContain("Include subfolders", form.Markup);
        // the helper text of the select tells what the chosen value does
        Assert.Contains("The file as an attachment, plus its name, size and date", form.Markup);

        form.Find(".gview-pipe-advanced .mud-expand-panel-header").Click();
        Assert.Contains("Include subfolders", form.Markup);
        Assert.Contains("Attachment name", form.Markup);
    }
}
