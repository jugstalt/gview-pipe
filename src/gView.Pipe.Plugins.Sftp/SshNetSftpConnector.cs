using gView.Pipe.Abstraction;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using System.Text;

namespace gView.Pipe.Plugins.Sftp;

/// <summary>The SSH.NET side of the SFTP node (ADR 0037): a thin adapter that is not tested against a real server.</summary>
internal sealed class SshNetSftpConnector : ISftpConnector
{
    public async Task<ISftpConnection> ConnectAsync(SftpCredential credential, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(credential.PrivateKey))
        {
            try
            {
                using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(credential.PrivateKey));
                var key = string.IsNullOrEmpty(credential.Passphrase) ? new PrivateKeyFile(keyStream) : new PrivateKeyFile(keyStream, credential.Passphrase);
                methods.Add(new PrivateKeyAuthenticationMethod(credential.UserName, key));
            }
            catch (Exception ex) when (ex is SshException or ArgumentException or IOException)
            {
                throw new PipeException("The private key of the credential can't be read (format or passphrase)");
            }
        }
        if (!string.IsNullOrEmpty(credential.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(credential.UserName, credential.Password));
        }
        if (methods.Count == 0)
        {
            throw new PipeException("The credential needs a password or a private key");
        }

        var info = new ConnectionInfo(credential.Host, credential.Port, credential.UserName, [.. methods]) { Timeout = timeout };
        var client = new SftpClient(info) { OperationTimeout = timeout };

        var expected = Normalize(credential.HostKeyFingerprint);
        string? presented = null;
        client.HostKeyReceived += (_, e) =>
        {
            presented = Normalize(e.FingerPrintSHA256);
            e.CanTrust = string.Equals(presented, expected, StringComparison.Ordinal);
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            if (presented is not null && !string.Equals(presented, expected, StringComparison.Ordinal))
            {
                throw new PipeException($"The host key of {credential.Host} doesn't match the fingerprint of the credential; the server presented SHA256:{presented}");
            }
            // the message of the library, but never the secrets of the credential
            throw new PipeException($"The connection to {credential.Host}:{credential.Port} failed ({ex.GetType().Name}: {ex.Message})");
        }

        return new Connection(client);
    }

    // "SHA256:abc=" and "abc" are the same
    static private string Normalize(string? fingerprint)
    {
        var text = (fingerprint ?? "").Trim();
        if (text.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["SHA256:".Length..];
        }
        return text.TrimEnd('=');
    }

    private sealed class Connection : ISftpConnection
    {
        private readonly SftpClient _client;

        public Connection(SftpClient client)
        {
            _client = client;
        }

        public async Task<SftpEntry?> StatAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                return ToEntry(await _client.GetAsync(path, cancellationToken));
            }
            catch (SftpPathNotFoundException)
            {
                return null;
            }
        }

        public async Task<IReadOnlyList<SftpEntry>> ListAsync(string path, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken)
        {
            var result = new List<SftpEntry>();
            await ListInto(path, includeSubfolders, maxEntries, result, cancellationToken);
            return result;
        }

        private async Task ListInto(string path, bool includeSubfolders, int maxEntries, List<SftpEntry> result, CancellationToken cancellationToken)
        {
            await foreach (var file in _client.ListDirectoryAsync(path, cancellationToken))
            {
                if (file.Name is "." or "..")
                {
                    continue;
                }

                if (result.Count >= maxEntries)
                {
                    throw new PipeException($"'{path}' has more than {maxEntries} entries; narrow the path or the pattern");
                }
                result.Add(ToEntry(file));

                if (includeSubfolders && file.IsDirectory)
                {
                    await ListInto(file.FullName, true, maxEntries, result, cancellationToken);
                }
            }
        }

        public Task DownloadAsync(string path, Stream destination, CancellationToken cancellationToken)
            => _client.DownloadFileAsync(path, destination, cancellationToken);

        public Task UploadAsync(string path, Stream source, CancellationToken cancellationToken)
            => _client.UploadFileAsync(source, path, cancellationToken);

        public Task CreateFolderAsync(string path, CancellationToken cancellationToken)
            => _client.CreateDirectoryAsync(path, cancellationToken);

        public Task DeleteAsync(string path, CancellationToken cancellationToken)
            => _client.DeleteFileAsync(path, cancellationToken);

        public Task RenameAsync(string path, string newPath, CancellationToken cancellationToken)
            => _client.RenameFileAsync(path, newPath, cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
            _client.Dispose();
            return ValueTask.CompletedTask;
        }

        static private SftpEntry ToEntry(ISftpFile file)
            => new(file.FullName, file.Name, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), file.IsDirectory);
    }
}
