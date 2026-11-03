using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Secrets;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Variables;
using gView.Pipe.Abstraction.Workflows;
using gView.Pipe.Core.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace gView.Pipe.Core.Tests.Storage;

public sealed class PipeSecretTests
{
    [Theory]
    [InlineData("secret:vault/db-password", true, "vault", "db-password")]
    [InlineData("secret:vault/db-password@3", true, "vault", "db-password@3")]
    [InlineData("secret:vault", false, null, null)]
    [InlineData("secret:/x", false, null, null)]
    [InlineData("plain", false, null, null)]
    public void References_are_parsed(string value, bool ok, string? source, string? name)
    {
        Assert.Equal(ok, PipeSecretReference.TryParse(value, out var reference));
        if (ok)
        {
            Assert.Equal(source, reference.Source);
            Assert.Equal(name, reference.Name);
        }
    }

    // the env source reads PIPE_SECRET_<NAME>; set per test with a name no other test uses
    private sealed class FakeProvider(Dictionary<string, string?> values) : IPipeSecretProvider
    {
        public int Reads { get; private set; }

        public Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(values.GetValueOrDefault(name));
        }
    }

    private sealed class FakeFactory(FakeProvider provider) : IPipeSecretProviderFactory
    {
        public string Type => "fake";
        public IPipeSecretProvider Create(string sourceName, IReadOnlyDictionary<string, string?> settings) => provider;
    }

    private static ServiceProvider Host(FakeProvider provider, int cacheSeconds = 300)
    {
        var services = new ServiceCollection();
        services.AddPipeCore(options =>
        {
            options.ScanApplicationDirectory = false;
            options.PluginPath = null;
            options.PluginAssemblies.Add(typeof(PipeTestHost).Assembly);
            options.Secrets.CacheSeconds = cacheSeconds;
            options.Secrets.Sources["vault"] = new Core.Options.PipeSecretSourceOptions { Type = "fake" };
        });
        services.AddSingleton<IPipeSecretProviderFactory>(new FakeFactory(provider));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_environment_source_exists_without_configuration_and_needs_its_prefix()
    {
        Environment.SetEnvironmentVariable("PIPE_SECRET_TEST_DB_PASSWORD", "s3cret!");
        using var host = PipeTestHost.Create();
        var resolver = host.GetRequiredService<IPipeSecretResolver>();

        Assert.Contains("env", resolver.Sources);
        Assert.Equal("s3cret!", await resolver.ResolveAsync(new PipeSecretReference("env", "test-db.password")));

        var missing = await Assert.ThrowsAsync<PipeException>(() => resolver.ResolveAsync(new PipeSecretReference("env", "nothing-here")));
        Assert.Contains("PIPE_SECRET_NOTHING_HERE", missing.Message);

        // a name can't leave the prefix: PATH would be PIPE_SECRET_PATH
        await Assert.ThrowsAsync<PipeException>(() => resolver.ResolveAsync(new PipeSecretReference("env", "x@1")));
    }

    [Fact]
    public async Task A_value_is_cached_for_the_configured_time_only()
    {
        var provider = new FakeProvider(new() { ["pw"] = "one" });
        using var cached = Host(provider);
        var resolver = cached.GetRequiredService<IPipeSecretResolver>();
        var reference = new PipeSecretReference("vault", "pw");

        await resolver.ResolveAsync(reference);
        await resolver.ResolveAsync(reference);
        Assert.Equal(1, provider.Reads);

        var uncachedProvider = new FakeProvider(new() { ["pw"] = "one" });
        using var uncached = Host(uncachedProvider, cacheSeconds: 0);
        await uncached.GetRequiredService<IPipeSecretResolver>().ResolveAsync(reference);
        await uncached.GetRequiredService<IPipeSecretResolver>().ResolveAsync(reference);
        Assert.Equal(2, uncachedProvider.Reads);
    }

    [Fact]
    public async Task A_missing_secret_or_source_fails_with_a_message_without_values()
    {
        using var host = Host(new FakeProvider(new()));
        var resolver = host.GetRequiredService<IPipeSecretResolver>();

        var missing = await Assert.ThrowsAsync<PipeException>(() => resolver.ResolveAsync(new PipeSecretReference("vault", "pw")));
        Assert.Contains("'pw'", missing.Message);
        Assert.Contains("'vault'", missing.Message);

        await Assert.ThrowsAsync<PipeException>(() => resolver.ResolveAsync(new PipeSecretReference("nowhere", "pw")));
        Assert.False((await resolver.CheckAsync(new PipeSecretReference("vault", "pw"))).Found);
    }

    [Fact]
    public async Task Variables_and_credential_fields_are_resolved_and_masked()
    {
        var provider = new FakeProvider(new() { ["pw"] = "very-secret-value" });
        using var host = Host(provider);

        var workflow = new PipeWorkflow
        {
            Id = "w",
            Name = "W",
            Variables = [new PipeVariable { Name = "DB_PASSWORD", Values = { [PipeEnvironments.Default] = "secret:vault/pw" } }]
        };
        var resolved = await host.GetRequiredService<IPipeEnvironments>().ResolveVariablesAsync(workflow, PipeEnvironments.Default);

        Assert.Equal("very-secret-value", resolved.Values["DB_PASSWORD"]);
        Assert.Contains("very-secret-value", resolved.SecretValues);

        var credentials = host.GetRequiredService<IPipeCredentialStore>();
        await credentials.SaveAsync(new PipeCredential
        {
            Id = "c",
            Name = "C",
            Data = new JsonObject { ["password"] = "secret:vault/pw", ["user"] = "sa" }
        });
        var stored = await credentials.GetAsync("c");
        Assert.Equal("secret:vault/pw", stored!.Data["password"]!.GetValue<string>());
    }
}
