using gView.Pipe.Docs.Screenshots;
using Microsoft.Playwright;

// dotnet run --project src/gView.Pipe.Docs.Screenshots [-- <output folder>]
// dotnet run --project src/gView.Pipe.Docs.Screenshots -- --install     (downloads Chromium once, about 150 MB)
if (args.Contains("--install"))
{
    return Microsoft.Playwright.Program.Main(["install", "chromium"]);
}

var repositoryRoot = FindRepositoryRoot();
var output = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(repositoryRoot, "docs", "site", "images");
Directory.CreateDirectory(output);

await using var shell = await ShellProcess.StartAsync(repositoryRoot);
Console.WriteLine($"Shell at {shell.Address}");

try
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        Locale = "en-US",
        ColorScheme = ColorScheme.Light
    });
    var page = await context.NewPageAsync();

    await new DocsScreenshots(page, shell.Address, output).TakeAllAsync();
}
catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
{
    Console.Error.WriteLine("Chromium is not installed. Run once: dotnet run --project src/gView.Pipe.Docs.Screenshots -- --install");
    return 1;
}

return 0;

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "gView.Pipe.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("gView.Pipe.slnx not found");
}
