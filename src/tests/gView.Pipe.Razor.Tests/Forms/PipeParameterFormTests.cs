using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Plugins;
using gView.Pipe.Plugins.Nodes.Network;
using gView.Pipe.Plugins.Nodes.Transform;
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
    public void Editing_a_dictionary_value_writes_typed_json_into_the_parameters()
    {
        var parameters = new JsonObject { ["fields"] = new JsonObject { ["count"] = "1" } };
        var changes = 0;
        var form = RenderForm(typeof(SetNodeParameters), parameters, () => changes++);

        var valueInput = form.FindAll("input").Single(i => i.GetAttribute("value") == "1");
        valueInput.Change("7");

        Assert.Equal(7, parameters["fields"]!["count"]!.GetValue<long>());
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Expressions_in_text_fields_stay_strings()
    {
        var parameters = new JsonObject { ["url"] = "https://old" };
        var form = RenderForm(typeof(HttpRequestNodeParameters), parameters);

        form.FindAll("input").Single(i => i.GetAttribute("value") == "https://old").Change("https://x/{{ $json.id }}");

        Assert.Equal("https://x/{{ $json.id }}", parameters["url"]!.GetValue<string>());
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
}
