using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Secrets;

namespace gView.Pipe.Plugins.Secrets;

public sealed class PipeAzureKeyVaultSecretProviderFactory : IPipeSecretProviderFactory
{
    public string Type => "azureKeyVault";

    public IPipeSecretProvider Create(string sourceName, IReadOnlyDictionary<string, string?> settings)
        => new PipeAzureKeyVaultSecretProvider(
            sourceName,
            new SecretClient(new Uri(PipeSecretSettings.Required(sourceName, settings, "VaultUri")), new DefaultAzureCredential()),
            settings.GetValueOrDefault("Prefix") ?? "pipe-");
}

/// <summary>
/// Azure Key Vault, signed in with <see cref="DefaultAzureCredential"/> (managed identity in Azure, the developer login locally).
/// The name of a reference gets the configured prefix, so a workflow reaches only the secrets that start with it.
/// </summary>
internal sealed class PipeAzureKeyVaultSecretProvider : IPipeSecretProvider
{
    private readonly string _sourceName;
    private readonly SecretClient _client;
    private readonly string _prefix;

    public PipeAzureKeyVaultSecretProvider(string sourceName, SecretClient client, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new PipeException($"Secret source '{sourceName}': the setting Prefix must not be empty");
        }
        _sourceName = sourceName;
        _client = client;
        _prefix = prefix;
    }

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var (secretName, version) = PipeSecretSettings.SplitVersion(name);
        // Key Vault names allow letters, digits and '-' only
        if (secretName.Length == 0 || !secretName.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new PipeException($"Invalid secret name '{secretName}' for Key Vault: use letters, digits and '-'");
        }

        try
        {
            var response = await _client.GetSecretAsync(_prefix + secretName, version, cancellationToken);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex)
        {
            throw new PipeException($"Secret source '{_sourceName}' answered {ex.Status} ({ex.ErrorCode}) for '{secretName}'");
        }
        catch (AuthenticationFailedException)
        {
            throw new PipeException($"Secret source '{_sourceName}': the sign-in to Azure failed");
        }
    }
}
