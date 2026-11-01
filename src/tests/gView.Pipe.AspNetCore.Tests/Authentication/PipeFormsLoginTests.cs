using System.Net;
using gView.Pipe.AspNetCore.Authentication;
using gView.Pipe.AspNetCore.Extensions.Builder;
using gView.Pipe.AspNetCore.Extensions.DependencyInjection;
using gView.Pipe.AspNetCore.Extensions.Routing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.AspNetCore.Tests.Authentication;

/// <summary>The forms login end to end, with a protected endpoint standing in for the Razor components.</summary>
public class PipeFormsLoginTests
{
    static private async Task<WebApplication> StartAsync(PipeAuthenticationMode mode = PipeAuthenticationMode.Forms, int attempts = 10)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAntiforgery();
        builder.Services.AddPipeAuthentication(options =>
        {
            options.Mode = mode;
            options.LoginAttemptsPerMinute = attempts;
            options.Users.Add(new PipeFormsUser { Name = "admin", PasswordHash = PipePasswordHasher.Hash("s3cret") });
        });

        var app = builder.Build();
        app.UsePipeAuthentication();
        app.UseAntiforgery();
        app.MapPipeAuthentication();
        app.MapGet("/ui", (HttpContext context) => $"hello {context.User.Identity?.Name}").RequirePipeAuthorization();
        // the host's login page renders the token; here an endpoint hands it out
        app.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken);

        await app.StartAsync();
        return app;
    }

    static private HttpClient Client(WebApplication app)
    {
        var server = app.GetTestServer();
        server.BaseAddress = new Uri("https://localhost/");
        // keeps the cookies (antiforgery, login) like a browser, but no redirects
        return new HttpClient(new CookieContainerHandler(server.CreateHandler())) { BaseAddress = server.BaseAddress };
    }

    static private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password, string returnUrl = "/ui")
    {
        var token = await client.GetStringAsync("/token");
        return await client.PostAsync("/account/signin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["username"] = "Admin",
            ["password"] = password,
            ["returnUrl"] = returnUrl
        }));
    }

    [Fact]
    public async Task Anonymous_IsRedirectedToTheLogin()
    {
        await using var app = await StartAsync();
        var client = Client(app);

        var response = await client.GetAsync("/ui");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Login_WithTheRightPassword_OpensTheUi()
    {
        await using var app = await StartAsync();
        var client = Client(app);

        var login = await LoginAsync(client, "s3cret");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/ui", login.Headers.Location!.OriginalString);

        Assert.Equal("hello admin", await client.GetStringAsync("/ui"));

        var logout = await client.GetAsync("/account/logout");
        Assert.Equal("/account/login", logout.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ui")).StatusCode);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_ReturnsToTheFormWithAnError()
    {
        await using var app = await StartAsync();
        var client = Client(app);

        var login = await LoginAsync(client, "wrong");

        Assert.StartsWith("/account/login?error=1", login.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/ui")).StatusCode);
    }

    [Fact]
    public async Task Login_IgnoresForeignReturnUrls()
    {
        await using var app = await StartAsync();
        var client = Client(app);

        var login = await LoginAsync(client, "s3cret", "https://evil.example/");

        Assert.Equal("/", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_IsRejected()
    {
        await using var app = await StartAsync();
        var client = Client(app);

        var response = await client.PostAsync("/account/signin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "s3cret"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_IsRateLimited()
    {
        await using var app = await StartAsync(attempts: 1);
        var client = Client(app);

        await LoginAsync(client, "s3cret");
        var second = await LoginAsync(client, "s3cret");

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task ModeNone_LetsEveryoneIn()
    {
        await using var app = await StartAsync(PipeAuthenticationMode.None);
        var client = Client(app);

        Assert.Equal("hello ", await client.GetStringAsync("/ui"));
    }

    private class CookieContainerHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer _cookies = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = _cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    _cookies.SetCookies(request.RequestUri!, setCookie);
                }
            }
            return response;
        }
    }
}
