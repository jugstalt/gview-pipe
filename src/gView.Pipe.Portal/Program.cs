using gView.Pipe.AspNetCore.Authentication;
using gView.Pipe.AspNetCore.Extensions.Builder;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
using gView.Pipe.AspNetCore.Extensions.Routing;
using gView.Pipe.Portal.Components;
using gView.Pipe.Portal.Portal;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// a file next to the app for the settings of the installation, like _config/ of the Shell
builder.Configuration.AddJsonFile("portal.config.json", optional: true, reloadOnChange: false);

var portal = builder.Configuration.GetSection("Portal").Get<PipePortalOptions>() ?? new PipePortalOptions();
if (string.IsNullOrWhiteSpace(portal.BackendUrl) || string.IsNullOrWhiteSpace(portal.ApiKey))
{
    throw new InvalidOperationException("Portal:BackendUrl and Portal:ApiKey are needed (appsettings, portal.config.json or the environment: Portal__BackendUrl, Portal__ApiKey).");
}
builder.Services.AddSingleton(portal);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// the key lives here and goes only to the backend; no request of the browser carries it
builder.Services.AddHttpClient<PipePortalClient>(client =>
{
    client.BaseAddress = new Uri(portal.BackendUrl.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add(PipePortalClient.KeyHeader, portal.ApiKey);
    // a run the user waits for may take as long as its wait timeout (up to an hour)
    client.Timeout = TimeSpan.FromMinutes(65);
});

// the login of the users (ADR 0014), from the section Portal:Auth
builder.Services.AddPipeAuthentication(options => builder.Configuration.GetSection("Portal:Auth").Bind(options));

var app = builder.Build();

// first: handles the forwarded headers of a reverse proxy
app.UsePipeAuthentication();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapPipeAuthentication();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequirePipeAuthorization();

app.Run();
