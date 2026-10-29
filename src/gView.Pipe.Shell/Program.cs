using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Extensions.DependencyInjection;
using gView.Pipe.Shell.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services
    .AddPipeCore(options => options.PluginPath = builder.Configuration["Pipe:PluginPath"] ?? "plugins")
    .AddPipeFileStore(options =>
    {
        options.RootPath = builder.Configuration["Pipe:DataPath"] ?? "data";
        options.SeedWorkflowsPath = Path.Combine(AppContext.BaseDirectory, "samples", "workflows");
    })
    .AddPipeRazor();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
