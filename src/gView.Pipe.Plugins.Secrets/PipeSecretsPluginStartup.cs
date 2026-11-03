using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Plugins;
using gView.Pipe.Abstraction.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace gView.Pipe.Plugins.Secrets;

/// <summary>Registers the secret sources of this plugin (ADR 0033).</summary>
public sealed class PipeSecretsPluginStartup : IPipePluginStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IPipeSecretProviderFactory, PipeAzureKeyVaultSecretProviderFactory>();
        services.AddSingleton<IPipeSecretProviderFactory, PipeIdentityServerVaultSecretProviderFactory>();
    }
}

static internal class PipeSecretSettings
{
    static public string Required(string sourceName, IReadOnlyDictionary<string, string?> settings, string key)
        => settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new PipeException($"Secret source '{sourceName}': the setting {key} is required");

    /// <summary>Splits <c>name@version</c>; the version is null without it.</summary>
    static public (string Name, string? Version) SplitVersion(string name)
    {
        var at = name.IndexOf('@');
        return at < 0 ? (name, null) : (name[..at], at == name.Length - 1 ? null : name[(at + 1)..]);
    }
}
