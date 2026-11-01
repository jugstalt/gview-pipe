using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Extensions.DependencyInjection;
using gView.Pipe.Shell;
using gView.Pipe.Shell.Components;
using Microsoft.Extensions.Hosting.WindowsServices;
using MudBlazor.Services;

if (WindowsServiceHelpers.IsWindowsService())
{
    // a service starts in System32; _config and relative paths belong to the app folder
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}

PipeConfigSetup.TrySetup();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "gView.Pipe");
if (OperatingSystem.IsWindows())
{
    // event source registered by gview-pipe-service.bat; the service account can't create it
    builder.Services.Configure<Microsoft.Extensions.Logging.EventLog.EventLogSettings>(settings => settings.SourceName = "gView.Pipe");
}

builder.Configuration.AddPipeConfig();

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services
    .AddPipeCore(options =>
    {
        options.PluginPath = builder.Configuration["Pipe:PluginPath"] ?? "plugins";
        // folders released for Read File / Write File, by name
        foreach (var root in builder.Configuration.GetSection("Pipe:FileRoots").GetChildren().Where(r => !string.IsNullOrEmpty(r.Value)))
        {
            options.FileRoots[root.Key] = root.Value!;
        }
    })
    .AddPipeFileStore(options =>
    {
        options.RootPath = builder.Configuration["Pipe:DataPath"] ?? "data";
        options.SeedWorkflowsPath = Path.Combine(AppContext.BaseDirectory, "samples", "workflows");
        options.SeedGroupsPath = Path.Combine(AppContext.BaseDirectory, "samples", "groups.json");
    })
    .AddPipeRazor();

var docs = PipeDocsSite.Find(builder.Configuration["Pipe:DocsPath"]);
builder.Services.AddSingleton(docs);

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapPipeWebhooks();
docs.Map(app);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
