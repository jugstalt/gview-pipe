namespace gView.Pipe.Abstraction.Files;

/// <summary>
/// Folders of the host that workflows may read and write, by name. Nodes never get arbitrary paths:
/// they name a root and a path relative to it.
/// </summary>
public interface IPipeFileRoots
{
    /// <summary>Names of the configured roots.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>
    /// Full path of <paramref name="relativePath"/> inside the root <paramref name="root"/>.
    /// </summary>
    /// <exception cref="PipeException">The root is unknown, or the path is absolute or leaves the root (e.g. with <c>..</c>).</exception>
    string Resolve(string root, string relativePath);
}
