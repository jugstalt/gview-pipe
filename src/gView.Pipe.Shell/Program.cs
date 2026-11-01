using gView.Pipe.AspNetCore.Authentication;
using gView.Pipe.AspNetCore.Extensions.Builder;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Core.Extensions.DependencyInjection;
using gView.Pipe.Razor.Extensions.DependencyInjection;
using gView.Pipe.Shell;
using gView.Pipe.Shell.Components;
using Microsoft.Extensions.Hosting.WindowsServices;
using MudBlazor.Services;

if (args is ["--hash-password", var password])
{
    // for Pipe:Auth:Users[].PasswordHash in _config/auth.config
    Console.WriteLine(PipePasswordHasher.Hash(password));
    return;
}

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
        // environments and instance variables (ADR 0013); Pipe__Variables__NAME__PROD works in Docker
        options.Environment = builder.Configuration["Pipe:Environment"] ?? "DEFAULT";
        options.Environments.AddRange(builder.Configuration.GetSection("Pipe:Environments").Get<string[]>() ?? []);
        options.SecretVariables.AddRange(builder.Configuration.GetSection("Pipe:SecretVariables").Get<string[]>() ?? []);
        foreach (var variable in builder.Configuration.GetSection("Pipe:Variables").GetChildren())
        {
            options.Variables[variable.Key] = variable.GetChildren()
                .Where(v => v.Value is not null)
                .ToDictionary(v => v.Key, v => v.Value!, StringComparer.OrdinalIgnoreCase);
        }
    })
    .AddPipeFileStore(options =>
    {
        options.RootPath = builder.Configuration["Pipe:DataPath"] ?? "data";
        options.SeedWorkflowsPath = Path.Combine(AppContext.BaseDirectory, "samples", "workflows");
        options.SeedGroupsPath = Path.Combine(AppContext.BaseDirectory, "samples", "groups.json");
    })
    .AddPipeRazor();

// login of the UI (ADR 0014), from _config/auth.config
builder.Services.AddPipeAuthentication(options =>
{
    builder.Configuration.GetSection("Pipe:Auth").Bind(options);

    // Docker shortcut: PIPE_ADMIN_PASSWORD sets the password of "admin" (or PIPE_ADMIN_USER) and switches none to forms
    if (Environment.GetEnvironmentVariable("PIPE_ADMIN_PASSWORD") is { Length: > 0 } adminPassword)
    {
        var adminName = Environment.GetEnvironmentVariable("PIPE_ADMIN_USER") is { Length: > 0 } name ? name : "admin";
        options.Users.RemoveAll(u => string.Equals(u.Name, adminName, StringComparison.OrdinalIgnoreCase));
        options.Users.Add(new PipeFormsUser { Name = adminName, Password = adminPassword });
        if (options.Mode == PipeAuthenticationMode.None)
        {
            options.Mode = PipeAuthenticationMode.Forms;
        }
    }
});

var docs = PipeDocsSite.Find(builder.Configuration["Pipe:DocsPath"]);
builder.Services.AddSingleton(docs);

var app = builder.Build();

app.MapDefaultEndpoints();

// first: handles the forwarded headers of a reverse proxy
app.UsePipeAuthentication();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapPipeWebhooks();
app.MapPipeAuthentication();
docs.Map(app);

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // webhooks, docs and static files stay open; the login pages are [AllowAnonymous]
    .RequirePipeAuthorization();

app.Run();
