using gView.Pipe.Abstraction.Security;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;

namespace gView.Pipe.Core.Storage;

/// <summary>API keys in memory; with a file, every change is written to <c>{root}/api-keys.json</c> (ADR 0020).</summary>
internal class PipeMemoryApiKeyStore : IPipeApiKeyStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<PipeApiKey>? _keys;

    public async Task<IReadOnlyList<PipeApiKey>> ListAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken)).Select(PipeJson.Clone).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<PipeApiKey?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken)).FirstOrDefault(k => string.Equals(k.Hash, hash, StringComparison.Ordinal)) is { } key
            ? PipeJson.Clone(key)
            : null;

    public async Task SaveAsync(PipeApiKey key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key.Id))
        {
            key.Id = PipeStoreIds.NewId();
        }
        await ChangeAsync(keys =>
        {
            keys.RemoveAll(k => k.Id == key.Id);
            keys.Add(PipeJson.Clone(key));
        }, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        => ChangeAsync(keys => keys.RemoveAll(k => k.Id == id), cancellationToken);

    protected virtual Task<List<PipeApiKey>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new List<PipeApiKey>());

    protected virtual Task WriteAsync(List<PipeApiKey> keys, CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<List<PipeApiKey>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_keys is not null)
        {
            return _keys;
        }
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return _keys ??= await ReadAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ChangeAsync(Action<List<PipeApiKey>> change, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // copy on write: readers keep a consistent list
            var keys = new List<PipeApiKey>(_keys!);
            change(keys);
            await WriteAsync(keys, cancellationToken);
            _keys = keys;
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed class PipeFileApiKeyStore : PipeMemoryApiKeyStore
{
    private readonly string _file;

    public PipeFileApiKeyStore(IOptions<PipeFileStoreOptions> options)
    {
        _file = Path.Combine(Path.GetFullPath(options.Value.RootPath), "api-keys.json");
    }

    protected override async Task<List<PipeApiKey>> ReadAsync(CancellationToken cancellationToken)
        => File.Exists(_file)
            ? PipeJson.Deserialize<List<PipeApiKey>>(await File.ReadAllTextAsync(_file, cancellationToken)) ?? []
            : [];

    protected override Task WriteAsync(List<PipeApiKey> keys, CancellationToken cancellationToken)
        => PipeFiles.WriteAtomicAsync(_file, PipeJson.Serialize(keys), cancellationToken);
}
