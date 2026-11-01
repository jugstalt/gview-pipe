using Microsoft.AspNetCore.Http.Headers;
using Microsoft.Extensions.FileProviders;

namespace gView.Pipe.Shell;

/// <summary>
/// The documentation (static site, built by the Docs target of the deploy build) served under <c>/docs</c>.
/// Without a built site the folder is missing and nothing is served.
/// </summary>
public sealed class PipeDocsSite
{
    private PipeDocsSite(string? path)
    {
        Path = path;
    }

    /// <summary>Folder with the built site, or null when there is none.</summary>
    public string? Path { get; }

    public bool Available => Path is not null;

    /// <param name="configuredPath"><c>Pipe:DocsPath</c>; relative paths are resolved against the application folder.</param>
    static public PipeDocsSite Find(string? configuredPath)
    {
        var path = System.IO.Path.GetFullPath(configuredPath ?? "docs", AppContext.BaseDirectory);
        return new PipeDocsSite(File.Exists(System.IO.Path.Combine(path, "index.html")) ? path : null);
    }

    /// <summary>Static files under <c>/docs</c> and a redirect from <c>/docs</c> to the start page in the language of the browser.</summary>
    public void Map(WebApplication app)
    {
        if (Path is null)
        {
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path),
            RequestPath = "/docs"
        });

        app.MapGet("/docs", (HttpContext context) =>
        {
            var german = context.Request.GetTypedHeaders().AcceptLanguage
                .OrderByDescending(l => l.Quality ?? 1)
                .Select(l => l.Value.Value ?? "")
                .FirstOrDefault(l => l.StartsWith("de", StringComparison.OrdinalIgnoreCase) || l.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                ?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true;

            return Results.Redirect($"{context.Request.PathBase}/docs/{(german ? "de/" : "")}index.html");
        });
    }
}
