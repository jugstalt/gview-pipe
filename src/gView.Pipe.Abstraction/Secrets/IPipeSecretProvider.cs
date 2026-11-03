namespace gView.Pipe.Abstraction.Secrets;

/// <summary>A source of secret values: environment variables, a key vault (ADR 0033). Read only.</summary>
public interface IPipeSecretProvider
{
    /// <summary>
    /// The value of the secret, or null when the source has none with this name.
    /// <paramref name="name"/> may end in <c>@version</c> for sources with versions.
    /// </summary>
    /// <exception cref="PipeException">The name is not allowed, or the source can't be reached.</exception>
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates the provider for a configured source of one type (<c>Pipe:Secrets:Providers:&lt;name&gt;:Type</c>).
/// Plugins register their factories in an <see cref="Plugins.IPipePluginStartup"/>.
/// </summary>
public interface IPipeSecretProviderFactory
{
    /// <summary>The <c>Type</c> in the configuration, e.g. <c>azureKeyVault</c>.</summary>
    string Type { get; }

    /// <param name="sourceName">The configured name of the source, for messages.</param>
    /// <param name="settings">The other settings of the source, case-insensitive.</param>
    IPipeSecretProvider Create(string sourceName, IReadOnlyDictionary<string, string?> settings);
}

/// <summary>Resolves references to values of the configured sources, with a cache in memory.</summary>
public interface IPipeSecretResolver
{
    /// <summary>Names of the configured sources, <c>env</c> included.</summary>
    IReadOnlyList<string> Sources { get; }

    /// <summary>The value; fails with a <see cref="PipeException"/> that names source and secret, never a value.</summary>
    Task<string> ResolveAsync(PipeSecretReference reference, CancellationToken cancellationToken = default);

    /// <summary>Reads the secret and tells whether it is there, without returning it.</summary>
    Task<PipeSecretCheck> CheckAsync(PipeSecretReference reference, CancellationToken cancellationToken = default);
}

public sealed record PipeSecretCheck(bool Found, string? Error = null);

/// <summary>
/// A field value that stands for a secret of a source: <c>secret:&lt;source&gt;/&lt;name&gt;</c>.
/// A plain value that starts with <c>secret:</c> can't be stored.
/// </summary>
public sealed record PipeSecretReference(string Source, string Name)
{
    public const string Prefix = "secret:";

    static public bool IsReference(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    static public bool TryParse(string? value, out PipeSecretReference reference)
    {
        reference = null!;
        if (!IsReference(value))
        {
            return false;
        }

        var rest = value![Prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
        {
            return false;
        }

        reference = new PipeSecretReference(rest[..slash].Trim(), rest[(slash + 1)..].Trim());
        return reference.Source.Length > 0 && reference.Name.Length > 0;
    }

    public override string ToString() => $"{Prefix}{Source}/{Name}";
}
