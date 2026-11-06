using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using gView.Pipe.Abstraction;

namespace gView.Pipe.Plugins.AzureStorage;

/// <summary>The Azure SDK side of node and trigger (ADR 0040): a thin adapter that is not tested against Azure.</summary>
internal sealed class AzureBlobConnector : IPipeBlobConnector
{
    public IPipeBlobSession Connect(AzureStorageCredential credential, TimeSpan timeout)
    {
        var options = new BlobClientOptions();
        options.Retry.NetworkTimeout = timeout;

        var service = credential.Authentication switch
        {
            AzureStorageAuthentication.ManagedIdentity => new BlobServiceClient(AccountUrl(credential), new DefaultAzureCredential(), options),
            AzureStorageAuthentication.SasToken => new BlobServiceClient(AccountUrl(credential),
                new AzureSasCredential(string.IsNullOrWhiteSpace(credential.SasToken)
                    ? throw new PipeException("The credential needs a SAS token")
                    : credential.SasToken.Trim().TrimStart('?')), options),
            _ => new BlobServiceClient(string.IsNullOrWhiteSpace(credential.ConnectionString)
                ? throw new PipeException("The credential needs a connection string")
                : credential.ConnectionString.Trim(), options)
        };
        return new Session(service);
    }

    static private Uri AccountUrl(AzureStorageCredential credential)
        => Uri.TryCreate(credential.AccountUrl?.Trim(), UriKind.Absolute, out var url)
            ? url
            : throw new PipeException("The credential needs the account URL, e.g. https://<account>.blob.core.windows.net");

    private sealed class Session : IPipeBlobSession
    {
        private readonly BlobServiceClient _service;

        public Session(BlobServiceClient service)
        {
            _service = service;
        }

        private BlobClient Blob(string container, string path) => _service.GetBlobContainerClient(container).GetBlobClient(path);

        public Task<PipeBlobEntry?> StatAsync(string container, string path, CancellationToken cancellationToken)
            => Call(async () =>
            {
                try
                {
                    var properties = (await Blob(container, path).GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
                    return new PipeBlobEntry(container, path, properties.ContentLength, properties.LastModified, properties.ContentType, properties.ETag.ToString());
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return (PipeBlobEntry?)null;
                }
            });

        public Task<IReadOnlyList<PipeBlobEntry>> ListAsync(string container, string prefix, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken)
            => Call(async () =>
            {
                var client = _service.GetBlobContainerClient(container);
                var result = new List<PipeBlobEntry>();

                void Add(BlobItem blob)
                {
                    if (result.Count >= maxEntries)
                    {
                        throw new PipeException($"'{container}/{prefix}' has more than {maxEntries} blobs; narrow the path or the pattern");
                    }
                    result.Add(new PipeBlobEntry(container, blob.Name, blob.Properties.ContentLength ?? 0,
                        blob.Properties.LastModified ?? DateTimeOffset.MinValue, blob.Properties.ContentType, blob.Properties.ETag?.ToString()));
                }

                if (includeSubfolders)
                {
                    await foreach (var blob in client.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
                    {
                        Add(blob);
                    }
                }
                else
                {
                    // only the blobs of this level; the "folders" below come as prefixes and are left out
                    await foreach (var entry in client.GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, "/", prefix, cancellationToken))
                    {
                        if (entry.IsBlob)
                        {
                            Add(entry.Blob);
                        }
                    }
                }
                return (IReadOnlyList<PipeBlobEntry>)result;
            });

        public Task DownloadAsync(string container, string path, Stream destination, CancellationToken cancellationToken)
            => Call(async () => (await Blob(container, path).DownloadToAsync(destination, cancellationToken)).Status);

        public Task UploadAsync(string container, string path, Stream content, string? contentType, bool overwrite, CancellationToken cancellationToken)
            => Call(async () =>
            {
                var options = new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType },
                    // the server refuses an existing blob in the same request, so two runs can't overwrite each other unnoticed
                    Conditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All }
                };
                try
                {
                    await Blob(container, path).UploadAsync(content, options, cancellationToken);
                }
                catch (RequestFailedException ex) when (!overwrite && ex.Status is 409 or 412)
                {
                    throw new PipeException($"'{container}/{path}' exists; switch on Overwrite to replace it");
                }
                return 0;
            });

        public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken)
            => Call(async () => (await _service.GetBlobContainerClient(container).ExistsAsync(cancellationToken)).Value);

        public Task CreateContainerAsync(string container, CancellationToken cancellationToken)
            => Call(async () => (await _service.GetBlobContainerClient(container).CreateIfNotExistsAsync(cancellationToken: cancellationToken))?.Value);

        public Task DeleteAsync(string container, string path, CancellationToken cancellationToken)
            => Call(async () => (await Blob(container, path).DeleteAsync(cancellationToken: cancellationToken)).Status);

        public Task CopyAsync(string container, string path, string targetContainer, string targetPath, bool overwrite, CancellationToken cancellationToken)
            => Call(async () =>
            {
                var target = Blob(targetContainer, targetPath);
                var options = new BlobCopyFromUriOptions
                {
                    DestinationConditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All }
                };
                try
                {
                    var copy = await target.StartCopyFromUriAsync(Blob(container, path).Uri, options, cancellationToken);
                    await copy.WaitForCompletionAsync(cancellationToken);
                }
                catch (RequestFailedException ex) when (!overwrite && ex.Status is 409 or 412)
                {
                    throw new PipeException($"'{targetContainer}/{targetPath}' exists; switch on Overwrite to replace it");
                }
                return 0;
            });

        // errors of the service with status and code; the message of the SDK may hold the request, never the key
        static private async Task<T> Call<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (RequestFailedException ex)
            {
                throw new PipeException($"Azure Storage answered {ex.Status} ({ex.ErrorCode ?? "no code"})");
            }
            catch (AuthenticationFailedException)
            {
                throw new PipeException("The sign-in to Azure failed (managed identity or developer login)");
            }
        }
    }
}
