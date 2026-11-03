namespace gView.Pipe.Plugins.Sftp;

/// <summary>A file or folder on the server.</summary>
public sealed record SftpEntry(string Path, string Name, long Size, DateTimeOffset ModifiedAt, bool IsDirectory);

/// <summary>Opens connections to an SFTP server (ADR 0037). The node talks only to this, so it is tested without a server.</summary>
public interface ISftpConnector
{
    /// <exception cref="gView.Pipe.Abstraction.PipeException">The server can't be reached, refuses the login or has another host key.</exception>
    Task<ISftpConnection> ConnectAsync(SftpCredential credential, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface ISftpConnection : IAsyncDisposable
{
    /// <returns>The entry, or null if the path doesn't exist.</returns>
    Task<SftpEntry?> StatAsync(string path, CancellationToken cancellationToken);

    /// <summary>Entries below <paramref name="path"/>, without "." and "..".</summary>
    /// <exception cref="gView.Pipe.Abstraction.PipeException">There are more than <paramref name="maxEntries"/> entries.</exception>
    Task<IReadOnlyList<SftpEntry>> ListAsync(string path, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken);

    Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken);

    Task UploadAsync(string path, Stream source, CancellationToken cancellationToken);

    Task CreateFolderAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);

    Task RenameAsync(string path, string newPath, CancellationToken cancellationToken);
}
