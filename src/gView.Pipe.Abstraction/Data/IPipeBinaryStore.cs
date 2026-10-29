namespace gView.Pipe.Abstraction.Data;

/// <summary>
/// Holds binary content for the duration of an execution.
/// Small content is kept in memory, larger content is spilled to temp files.
/// </summary>
public interface IPipeBinaryStore
{
    Task<PipeBinaryRef> StoreAsync(Stream content, string fileName, string mimeType, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(PipeBinaryRef reference, CancellationToken cancellationToken = default);
}
