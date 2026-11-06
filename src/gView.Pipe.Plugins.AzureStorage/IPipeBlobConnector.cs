namespace gView.Pipe.Plugins.AzureStorage;

/// <summary>A blob of a container.</summary>
public sealed record PipeBlobEntry(string Container, string Path, long Size, DateTimeOffset ModifiedAt, string? ContentType, string? ETag)
{
    /// <summary>The last part of the path.</summary>
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];
}

/// <summary>Opens a session to a storage account (ADR 0040). Node and trigger talk only to this, so they are tested without Azure.</summary>
public interface IPipeBlobConnector
{
    /// <exception cref="gView.Pipe.Abstraction.PipeException">The credential is incomplete.</exception>
    IPipeBlobSession Connect(AzureStorageCredential credential, TimeSpan timeout);
}

/// <summary>Errors of the service come as <see cref="gView.Pipe.Abstraction.PipeException"/> with status and code, never with a secret.</summary>
public interface IPipeBlobSession
{
    /// <returns>The blob, or null if it doesn't exist.</returns>
    Task<PipeBlobEntry?> StatAsync(string container, string path, CancellationToken cancellationToken);

    /// <summary>The blobs below <paramref name="prefix"/> (a folder; empty for the whole container).</summary>
    /// <exception cref="gView.Pipe.Abstraction.PipeException">There are more than <paramref name="maxEntries"/> blobs.</exception>
    Task<IReadOnlyList<PipeBlobEntry>> ListAsync(string container, string prefix, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken);

    Task DownloadAsync(string container, string path, Stream destination, CancellationToken cancellationToken);

    /// <exception cref="gView.Pipe.Abstraction.PipeException">The blob exists and <paramref name="overwrite"/> is false; the server checks that in the same request.</exception>
    Task UploadAsync(string container, string path, Stream content, string? contentType, bool overwrite, CancellationToken cancellationToken);

    Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken);

    Task CreateContainerAsync(string container, CancellationToken cancellationToken);

    Task DeleteAsync(string container, string path, CancellationToken cancellationToken);

    /// <summary>Copies on the server and waits until the copy is done.</summary>
    Task CopyAsync(string container, string path, string targetContainer, string targetPath, bool overwrite, CancellationToken cancellationToken);
}
