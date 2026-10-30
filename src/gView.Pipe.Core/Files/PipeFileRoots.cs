using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Files;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;

namespace gView.Pipe.Core.Files;

internal sealed class PipeFileRoots : IPipeFileRoots
{
    private readonly Dictionary<string, string> _roots;

    public PipeFileRoots(IOptions<PipeCoreOptions> options)
    {
        // relative roots are resolved against the current directory, like the other paths of the options
        _roots = options.Value.FileRoots.ToDictionary(
            p => p.Key,
            p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Value)),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Names => _roots.Keys;

    public string Resolve(string root, string relativePath)
    {
        if (!_roots.TryGetValue(root.Trim(), out var rootPath))
        {
            throw new PipeException(_roots.Count == 0
                ? "No folders are released for workflows (PipeCoreOptions.FileRoots)"
                : $"Unknown folder '{root}'; available: {string.Join(", ", _roots.Keys)}");
        }

        var relative = relativePath.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative.StartsWith('/'))
        {
            throw new PipeException($"'{relativePath}' must be relative to the folder '{root}'");
        }

        var full = Path.GetFullPath(Path.Combine(rootPath, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(rootPath, comparison) && !full.StartsWith(rootPath + Path.DirectorySeparatorChar, comparison))
        {
            throw new PipeException($"'{relativePath}' leaves the folder '{root}'");
        }

        return full;
    }
}
