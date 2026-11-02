using gView.Pipe.Abstraction.Audit;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace gView.Pipe.Razor.Tests.Components;

public class PipeAuditListTests : BunitContext
{
    public PipeAuditListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
        });
    }

    [Fact]
    public async Task Lists_the_entries_newest_first_and_filters_by_area()
    {
        var log = Services.GetRequiredService<IPipeAuditLog>();
        await log.WriteAsync(new PipeAuditEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-2), Actor = "ada", Area = PipeAuditArea.Credential, Action = "created", ObjectName = "SMTP" });
        await log.WriteAsync(new PipeAuditEntry { Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1), Actor = "api:CI", Area = PipeAuditArea.Workflow, Action = "deployed", ObjectName = "Import", Details = "nodes added: HTTP Request" });

        Render<MudPopoverProvider>();
        var list = Render<PipeAuditList>();

        list.WaitForAssertion(() => Assert.Equal(2, list.FindAll("tr.gview-pipe-audit-row").Count));
        var rows = list.FindAll("tr.gview-pipe-audit-row");
        Assert.Contains("api:CI", rows[0].TextContent);
        Assert.Contains("nodes added: HTTP Request", rows[0].TextContent);
        Assert.Contains("SMTP", rows[1].TextContent);
    }
}
