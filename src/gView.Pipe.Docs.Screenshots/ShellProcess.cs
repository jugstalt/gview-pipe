using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace gView.Pipe.Docs.Screenshots;

/// <summary>The Shell, started from its build output with an empty data folder, so the screenshots never show real data and no real workflow runs.</summary>
internal sealed class ShellProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _workFolder;
    private readonly string _logFile;

    private ShellProcess(Process process, string workFolder, string logFile, Uri address)
    {
        _process = process;
        _workFolder = workFolder;
        _logFile = logFile;
        Address = address;
    }

    public Uri Address { get; }

    /// <summary>The <c>workflows</c> folder of the temporary data folder: files put here show up in the editor.</summary>
    public string WorkflowsFolder => Path.Combine(_workFolder, "data", "workflows");

    static public async Task<ShellProcess> StartAsync(string repositoryRoot)
    {
        var workFolder = Path.Combine(Path.GetTempPath(), "gview-pipe-docs-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workFolder);
        var logFile = Path.Combine(workFolder, "shell.log");

        var port = FreePort();
        var address = new Uri($"http://127.0.0.1:{port}");

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(repositoryRoot, "src", "gView.Pipe.Shell"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "run", "--no-build", "--no-launch-profile", "--urls", address.ToString() })
        {
            start.ArgumentList.Add(argument);
        }

        // Development: static web assets (MudBlazor, diagram CSS) are served from the build output
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Pipe__DataPath"] = Path.Combine(workFolder, "data");
        start.Environment["Pipe__FileRoots__files"] = Path.Combine(workFolder, "files");
        start.Environment["Pipe__Scripts__ScriptRoots__scripts"] = Path.Combine(workFolder, "scripts");
        start.Environment["Pipe__Scripts__RepositoriesPath"] = Path.Combine(workFolder, "data", "script-repos");
        // a production instance with test environments and a variable, for the pictures of variables and environments
        start.Environment["Pipe__Environment"] = "PROD";
        start.Environment["Pipe__Environments__0"] = "TEST";
        start.Environment["Pipe__Environments__1"] = "STAGE";
        start.Environment["Pipe__Environments__2"] = "PROD";
        start.Environment["Pipe__Variables__DATABASE_SERVER__DEFAULT"] = "db-test";
        start.Environment["Pipe__Variables__DATABASE_SERVER__PROD"] = "db-prod";

        var process = Process.Start(start) ?? throw new InvalidOperationException("Can't start the Shell");
        var log = new StreamWriter(logFile) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => log.WriteLine(e.Data);
        process.ErrorDataReceived += (_, e) => log.WriteLine(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var shell = new ShellProcess(process, workFolder, logFile, address);
        await shell.WaitUntilReadyAsync();
        return shell;
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient();
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"The Shell stopped (exit code {_process.ExitCode}). Is it built? Log:{Environment.NewLine}{ReadLog()}");
            }

            try
            {
                using var response = await client.GetAsync(Address);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"The Shell didn't answer in 60 seconds. Log:{Environment.NewLine}{ReadLog()}");
    }

    private string ReadLog()
    {
        try
        {
            using var stream = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return "";
        }
    }

    static private int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        _process.Dispose();

        try
        {
            Directory.Delete(_workFolder, recursive: true);
        }
        catch (IOException)
        {
            // temp folder: the OS cleans it up
        }
    }
}
