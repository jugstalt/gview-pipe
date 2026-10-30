using gView.Pipe.Abstraction;

namespace gView.Pipe.Plugins.Scripts;

/// <summary>Which program runs a script, by its extension. Only what the host's operating system can run is offered.</summary>
static internal class PipeScriptInterpreters
{
    static public IReadOnlyList<string> Extensions { get; } = OperatingSystem.IsWindows()
        ? [".bat", ".cmd", ".ps1", ".py"]
        : [".ps1", ".py", ".sh"];

    static public bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Program and command line for the script; <paramref name="arguments"/> are appended as typed.</summary>
    static public (string FileName, string Arguments) CommandFor(string script, string? arguments, PipeScriptsOptions options)
    {
        var extension = Path.GetExtension(script).ToLowerInvariant();
        if (!IsSupported(script))
        {
            throw new PipeException($"'{Path.GetFileName(script)}': {extension} scripts can't run on this host (supported: {string.Join(", ", Extensions)})");
        }

        var configured = options.Interpreters.GetValueOrDefault(extension);
        var args = string.IsNullOrWhiteSpace(arguments) ? "" : " " + arguments.Trim();

        return extension switch
        {
            // /s: cmd strips exactly the outer quotes, so quoted paths with spaces survive
            ".bat" or ".cmd" => (configured ?? "cmd.exe", $"/d /s /c \"\"{script}\"{args}\""),
            ".ps1" => (configured ?? PowerShell(), $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\"{args}"),
            ".py" => (configured ?? "python", $"-u \"{script}\"{args}"),
            _ => (configured ?? "bash", $"\"{script}\"{args}")
        };
    }

    /// <summary>PowerShell 7 if installed, Windows PowerShell otherwise.</summary>
    static private string PowerShell()
        => FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh") ?? (OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh");

    static public string? FindOnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim('"'), program))
            .FirstOrDefault(File.Exists);
}
