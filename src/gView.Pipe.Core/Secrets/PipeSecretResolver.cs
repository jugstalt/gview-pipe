using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Secrets;
using gView.Pipe.Core.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace gView.Pipe.Core.Secrets;

/// <summary>Resolves references to the configured sources (ADR 0033). Values stay in memory only, for <see cref="PipeSecretOptions.CacheSeconds"/>.</summary>
internal sealed class PipeSecretResolver : IPipeSecretResolver
{
    private readonly PipeSecretOptions _options;
    private readonly Dictionary<string, IPipeSecretProviderFactory> _factories;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private Dictionary<string, Func<IPipeSecretProvider>>? _sources;
    private readonly ConcurrentDictionary<string, IPipeSecretProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset Expires)> _cache = new();

    public PipeSecretResolver(IOptions<PipeCoreOptions> options, IEnumerable<IPipeSecretProviderFactory> factories, TimeProvider time)
    {
        _options = options.Value.Secrets;
        _factories = factories.ToDictionary(f => f.Type, StringComparer.OrdinalIgnoreCase);
        _time = time;
    }

    public IReadOnlyList<string> Sources => Configured().Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<string> ResolveAsync(PipeSecretReference reference, CancellationToken cancellationToken = default)
    {
        var key = $"{reference.Source.ToLowerInvariant()}/{reference.Name}";
        var now = _time.GetUtcNow();
        if (_options.CacheSeconds > 0 && _cache.TryGetValue(key, out var cached) && cached.Expires > now)
        {
            return cached.Value;
        }

        var value = await ProviderOf(reference.Source).GetAsync(reference.Name, cancellationToken)
            ?? throw new PipeException($"Secret '{reference.Name}' not found in source '{reference.Source}'{Hint(reference)}");

        if (_options.CacheSeconds > 0)
        {
            _cache[key] = (value, now.AddSeconds(_options.CacheSeconds));
        }
        return value;
    }

    public async Task<PipeSecretCheck> CheckAsync(PipeSecretReference reference, CancellationToken cancellationToken = default)
    {
        try
        {
            // a check wants the truth, not the cache
            var value = await ProviderOf(reference.Source).GetAsync(reference.Name, cancellationToken);
            return value is null ? new PipeSecretCheck(false, $"Secret '{reference.Name}' not found in source '{reference.Source}'{Hint(reference)}") : new PipeSecretCheck(true);
        }
        catch (PipeException ex)
        {
            return new PipeSecretCheck(false, ex.Message);
        }
    }

    // the environment source names the variable it looked for
    private string Hint(PipeSecretReference reference)
        => ProviderOf(reference.Source) is IPipeSecretLookupHint hint ? $" ({hint.Describe(reference.Name)})" : "";

    private IPipeSecretProvider ProviderOf(string source)
    {
        var sources = Configured();
        if (!sources.TryGetValue(source, out var create))
        {
            throw new PipeException($"Secret source '{source}' is not configured (Pipe:Secrets:Sources)");
        }
        return _providers.GetOrAdd(source, _ => create());
    }

    private Dictionary<string, Func<IPipeSecretProvider>> Configured()
    {
        lock (_lock)
        {
            if (_sources is not null)
            {
                return _sources;
            }

            var sources = new Dictionary<string, Func<IPipeSecretProvider>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, source) in _options.Sources)
            {
                var type = source.Type;
                var settings = new Dictionary<string, string?>(source.Settings, StringComparer.OrdinalIgnoreCase);
                sources[name] = () => _factories.TryGetValue(type, out var factory)
                    ? factory.Create(name, settings)
                    : throw new PipeException($"Secret source '{name}': unknown type '{type}' (is its plugin installed?)");
            }

            // exists without configuration, with the default prefix
            if (!sources.ContainsKey(PipeEnvironmentSecretProvider.SourceName))
            {
                sources[PipeEnvironmentSecretProvider.SourceName] = () => new PipeEnvironmentSecretProvider(PipeEnvironmentSecretProvider.DefaultPrefix);
            }

            return _sources = sources;
        }
    }
}

/// <summary>A provider that can say where it looks, for the message when a secret is missing.</summary>
internal interface IPipeSecretLookupHint
{
    string Describe(string name);
}
