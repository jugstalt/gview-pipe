using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Secrets;
using IdentityServerNET.Clients;

namespace gView.Pipe.Plugins.Secrets;

public sealed class PipeIdentityServerVaultSecretProviderFactory : IPipeSecretProviderFactory
{
    public string Type => "identityServerVault";

    public IPipeSecretProvider Create(string sourceName, IReadOnlyDictionary<string, string?> settings)
        => new PipeIdentityServerVaultSecretProvider(
            sourceName,
            PipeSecretSettings.Required(sourceName, settings, "Authority"),
            PipeSecretSettings.Required(sourceName, settings, "Locker"),
            PipeSecretSettings.Required(sourceName, settings, "ClientId"),
            PipeSecretSettings.Required(sourceName, settings, "ClientSecret"));
}

/// <summary>
/// The secrets vault of IdentityServer.NET. The client gets a token for the scope of its locker and can read only that locker,
/// which limits what a workflow reaches.
/// </summary>
internal sealed class PipeIdentityServerVaultSecretProvider : IPipeSecretProvider
{
    private readonly string _sourceName;
    private readonly string _authority;
    private readonly string _locker;
    private readonly SecretsVaultClient _client;
    private readonly SemaphoreSlim _open = new(1, 1);
    private bool _opened;

    public PipeIdentityServerVaultSecretProvider(string sourceName, string authority, string locker, string clientId, string clientSecret)
    {
        _sourceName = sourceName;
        _authority = authority;
        _locker = locker;
        _client = new SecretsVaultClient(clientId, clientSecret);
    }

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var (secretName, version) = PipeSecretSettings.SplitVersion(name);
        if (secretName.Length == 0 || secretName.Contains('/'))
        {
            throw new PipeException($"Invalid secret name '{secretName}': it must not be empty or contain '/'");
        }

        try
        {
            await OpenAsync();
            var response = await _client.GetSecret(version is null ? secretName : $"{secretName}/{version}");
            return response?.GetValue();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PipeException)
        {
            // not found and not reachable look alike to the client; the message names the source, never a value
            throw new PipeException($"Secret source '{_sourceName}' could not deliver '{secretName}' ({ex.GetType().Name}); check Authority, Locker and the client");
        }
    }

    private async Task OpenAsync()
    {
        if (_opened)
        {
            return;
        }

        await _open.WaitAsync();
        try
        {
            if (!_opened)
            {
                await _client.OpenLocker(_authority, _locker);
                _opened = true;
            }
        }
        finally
        {
            _open.Release();
        }
    }
}
