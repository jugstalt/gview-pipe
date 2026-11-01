using Microsoft.Playwright;

namespace gView.Pipe.Docs.Screenshots;

/// <summary>
/// The screenshots of the docs, one method each. File names are referenced from the pages, so don't rename them.
/// The sample workflows of an empty data folder are the content.
/// </summary>
internal sealed class DocsScreenshots
{
    private readonly IPage _page;
    private readonly Uri _address;
    private readonly string _output;

    public DocsScreenshots(IPage page, Uri address, string output)
    {
        _page = page;
        _address = address;
        _output = output;
    }

    public async Task TakeAllAsync()
    {
        await StartPageAsync();
        await GroupPageAsync();
        await EditorRunAsync();
        await NodePanelsAsync();
        await WebhookTriggerAsync();
        await CredentialsAsync();
    }

    private async Task StartPageAsync()
    {
        await LowAsync();
        await OpenAsync("/");
        await _page.GetByText("Samples").First.WaitForAsync();
        await ShotAsync("start-page");
    }

    private async Task GroupPageAsync()
    {
        await OpenAsync("/groups/samples");
        await _page.GetByText("Hello Pipe").First.WaitForAsync();
        await HighAsync();
        await ShotAsync("group-page");
    }

    /// <summary>Hello Pipe after a run: green nodes, the result chip and the execution list.</summary>
    private async Task EditorRunAsync()
    {
        await HighAsync();
        await OpenEditorAsync("hello-pipe");
        await RunAsync();
        await ShotAsync("editor-run");
    }

    /// <summary>The three tabs of a node, on the node "Set Greeting" of the run before.</summary>
    private async Task NodePanelsAsync()
    {
        await SelectNodeAsync("Set Greeting");
        await ShotAsync("node-parameters");

        await SelectTabAsync("Settings");
        await ShotAsync("node-settings");

        await SelectTabAsync("Run");
        await ShotAsync("node-run");
    }

    private async Task WebhookTriggerAsync()
    {
        await OpenEditorAsync("webhook-demo");
        await SelectNodeAsync("Webhook");
        await ShotAsync("webhook-trigger");
    }

    private async Task CredentialsAsync()
    {
        await LowAsync();
        await OpenAsync("/credentials");
        await _page.GetByText("Credentials").First.WaitForAsync();
        await ShotAsync("credentials");
    }

    private async Task OpenAsync(string path)
    {
        await _page.GotoAsync(new Uri(_address, path).ToString());
        // Blazor Server: the page is interactive once the circuit is up
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private async Task OpenEditorAsync(string workflowId)
    {
        await OpenAsync($"/workflows/{workflowId}");
        await _page.Locator(".diagram-node").First.WaitForAsync();
        await _page.WaitForTimeoutAsync(500);
    }

    private async Task RunAsync()
    {
        await _page.GetByRole(AriaRole.Button, new() { Name = "Run", Exact = true }).ClickAsync();
        await _page.GetByText("Run finished").WaitForAsync();
        // the toast covers the toolbar: close it (the mouse hovering over it keeps it open)
        var toast = _page.Locator(".mud-snackbar").First;
        await toast.GetByRole(AriaRole.Button).ClickAsync();
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });
        await _page.WaitForTimeoutAsync(300);
    }

    private async Task SelectNodeAsync(string name)
    {
        await _page.Locator(".diagram-node", new PageLocatorOptions { HasText = name }).First.ClickAsync();
        await _page.GetByRole(AriaRole.Tab, new() { Name = "Parameters" }).WaitForAsync();
        await _page.WaitForTimeoutAsync(300);
    }

    private async Task SelectTabAsync(string name)
    {
        // the name may carry a badge ("Run 1"): Playwright matches a part of the label
        var tab = _page.GetByRole(AriaRole.Tab, new() { Name = name }).First;
        await tab.ClickAsync();
        // the move the mouse made over the tab must not leave a hover state in the picture
        await _page.Mouse.MoveAsync(700, 600);
        await _page.WaitForTimeoutAsync(800);

        if (await tab.GetAttributeAsync("aria-selected") != "true")
        {
            throw new InvalidOperationException($"The tab '{name}' is not selected");
        }
    }

    /// <summary>List pages are short; a low viewport avoids a picture that is mostly empty.</summary>
    private Task LowAsync() => _page.SetViewportSizeAsync(1440, 420);

    private Task HighAsync() => _page.SetViewportSizeAsync(1440, 900);

    private async Task ShotAsync(string name)
    {
        var file = Path.Combine(_output, name + ".png");
        await _page.ScreenshotAsync(new PageScreenshotOptions { Path = file });
        Console.WriteLine($"  {name}.png");
    }
}
