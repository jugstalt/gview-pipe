namespace gView.Pipe.Core.Options;

public class PipeFileStoreOptions
{
    /// <summary>
    /// Root folder with <c>workflows/</c>, <c>executions/</c>, <c>credentials/</c> and <c>keys/</c>.
    /// Relative paths are resolved against the current directory.
    /// </summary>
    public string RootPath { get; set; } = "data";

    /// <summary>
    /// Folder with workflow JSON files copied into <c>workflows/</c> when it contains no workflow yet
    /// (first start). Null disables seeding.
    /// </summary>
    public string? SeedWorkflowsPath { get; set; }

    /// <summary>
    /// <c>groups.json</c> with the groups the seeded workflows are in; copied together with them
    /// when the store has no groups yet.
    /// </summary>
    public string? SeedGroupsPath { get; set; }

    /// <summary>
    /// Persist the ASP.NET Data Protection keys (used to encrypt credentials) to <c>keys/</c>.
    /// Turn off if the host configures Data Protection itself.
    /// </summary>
    public bool PersistDataProtectionKeys { get; set; } = true;
}
