using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Extensions.DependencyInjection;
using gView.Pipe.Shell;
using gView.Pipe.Shell.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

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
