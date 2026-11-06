using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.AzureStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class AzureBlobTests
{
    /// <summary>A storage account in memory: blobs by container and path, and a log of what node and trigger did.</summary>
    private sealed class FakeStorage : IPipeBlobConnector, IPipeBlobSession
    {
        private int _version;

        public Dictionary<(string Container, string Path), (byte[] Content, string? ContentType, string ETag, DateTimeOffset Modified)> Blobs { get; } = new();
        public HashSet<string> Containers { get; } = new();
        public List<string> Log { get; } = new();
        public AzureStorageCredential? Credential { get; private set; }
        public bool FailCopy { get; set; }

        public FakeStorage Add(string container, string path, string content = "x")
        {
            Containers.Add(container);
            Blobs[(container, path)] = (Encoding.UTF8.GetBytes(content), "text/plain", $"\"{++_version}\"", new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero).AddSeconds(_version));
            return this;
        }

        public List<string> Paths(string container) => Blobs.Keys.Where(k => k.Container == container).Select(k => k.Path).Order(StringComparer.Ordinal).ToList();

        public IPipeBlobSession Connect(AzureStorageCredential credential, TimeSpan timeout)
        {
            Credential = credential;
            return this;
        }

        private PipeBlobEntry Entry((string Container, string Path) key)
        {
            var blob = Blobs[key];
            return new PipeBlobEntry(key.Container, key.Path, blob.Content.Length, blob.Modified, blob.ContentType, blob.ETag);
        }

        public Task<PipeBlobEntry?> StatAsync(string container, string path, CancellationToken cancellationToken)
            => Task.FromResult(Blobs.ContainsKey((container, path)) ? Entry((container, path)) : null);

        public Task<IReadOnlyList<PipeBlobEntry>> ListAsync(string container, string prefix, bool includeSubfolders, int maxEntries, CancellationToken cancellationToken)
        {
            var entries = Blobs.Keys
                .Where(k => k.Container == container && k.Path.StartsWith(prefix, StringComparison.Ordinal)
                            && (includeSubfolders || k.Path.IndexOf('/', prefix.Length) < 0))
                .Select(Entry).ToList();
            return entries.Count > maxEntries
                ? throw new PipeException("too many")
                : Task.FromResult<IReadOnlyList<PipeBlobEntry>>(entries);
        }

        public async Task DownloadAsync(string container, string path, Stream destination, CancellationToken cancellationToken)
        {
            Log.Add($"download {container}/{path}");
            await destination.WriteAsync(Blobs[(container, path)].Content, cancellationToken);
        }

        public async Task UploadAsync(string container, string path, Stream content, string? contentType, bool overwrite, CancellationToken cancellationToken)
        {
            Log.Add($"upload {container}/{path}");
            if (!overwrite && Blobs.ContainsKey((container, path)))
            {
                throw new PipeException($"'{container}/{path}' exists; switch on Overwrite to replace it");
            }
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Blobs[(container, path)] = (buffer.ToArray(), contentType, $"\"{++_version}\"", DateTimeOffset.UtcNow);
        }

        public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken) => Task.FromResult(Containers.Contains(container));

        public Task CreateContainerAsync(string container, CancellationToken cancellationToken)
        {
            Log.Add($"create {container}");
            Containers.Add(container);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string container, string path, CancellationToken cancellationToken)
        {
            Log.Add($"delete {container}/{path}");
            Blobs.Remove((container, path));
            return Task.CompletedTask;
        }

        public Task CopyAsync(string container, string path, string targetContainer, string targetPath, bool overwrite, CancellationToken cancellationToken)
        {
            Log.Add($"copy {container}/{path} {targetContainer}/{targetPath}");
            if (FailCopy)
            {
                throw new PipeException("Azure Storage answered 403 (AuthorizationPermissionMismatch)");
            }
            if (!overwrite && Blobs.ContainsKey((targetContainer, targetPath)))
            {
                throw new PipeException($"'{targetContainer}/{targetPath}' exists; switch on Overwrite to replace it");
            }
            Blobs[(targetContainer, targetPath)] = Blobs[(container, path)];
            return Task.CompletedTask;
        }
    }

    #region node

    private sealed class Setup : IDisposable
    {
        public FakeStorage Storage { get; } = new();
        public ServiceProvider Services { get; }
        public string CredentialId { get; private set; } = "";

        public Setup()
        {
            Services = PluginTestHost.Create(
                s => s.Replace(ServiceDescriptor.Singleton<IPipeBlobConnector>(Storage)),
                o => o.PluginAssemblies.Add(typeof(AzureBlobNode).Assembly));
        }

        public async Task<Setup> WithCredentialAsync()
        {
            var credential = new PipeCredential
            {
                Name = "Storage",
                TypeId = Guid.Parse(AzureStorageCredential.TypeId),
                Data = PipeJson.ToJsonObject(new AzureStorageCredential { ConnectionString = "UseDevelopmentStorage=true" })
            };
            await Services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
            CredentialId = credential.Id;
            return this;
        }

        public async Task<PipeExecution> RunAsync(params object[] operations)
        {
            var builder = new PipeWorkflowBuilder().Node("start", PipeBuiltInNodes.ManualTrigger);
            var previous = "start";
            for (var i = 0; i < operations.Length; i++)
            {
                var json = PipeJson.ToJsonObject(operations[i]);
                json["credentialId"] = CredentialId;
                builder = builder.Node($"blob{i}", AzureBlobNode.NodeId, json).Connect(previous, $"blob{i}");
                previous = $"blob{i}";
            }
            return await Services.GetRequiredService<IPipeEngine>().ExecuteAsync(builder.Build());
        }

        public void Dispose() => Services.Dispose();
    }

    static private async Task<Setup> NewAsync(Action<FakeStorage>? prepare = null)
    {
        var setup = new Setup();
        prepare?.Invoke(setup.Storage);
        return await setup.WithCredentialAsync();
    }

    [Fact]
    public async Task List_returns_the_blobs_of_a_folder_that_match()
    {
        using var setup = await NewAsync(s => s.Add("in", "orders/a.csv").Add("in", "orders/B.CSV").Add("in", "orders/c.txt").Add("in", "orders/sub/d.csv").Add("in", "other.csv"));

        var execution = await setup.RunAsync(new { operation = "list", container = "in", path = "/orders", pattern = "*.csv" });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var items = execution.Output("blob0");
        Assert.Equal(["orders/B.CSV", "orders/a.csv"], items.Select(i => i["path"]!.GetValue<string>()));
        Assert.Equal("a.csv", items[1]["fileName"]!.GetValue<string>());
        Assert.Equal("in", items[1]["container"]!.GetValue<string>());
        Assert.NotNull(items[1]["etag"]);

        var deep = await setup.RunAsync(new { operation = "list", container = "in", path = "orders", includeSubfolders = true, pattern = "*.csv" });
        Assert.Equal(3, deep.Output("blob0").Count);
    }

    [Fact]
    public async Task Download_and_upload_move_the_content_and_upload_needs_a_container_and_overwrite()
    {
        using var setup = await NewAsync(s => s.Add("in", "a.csv", "hello").Add("out", "taken.csv", "old"));

        var copied = await setup.RunAsync(
            new { operation = "download", container = "in", path = "a.csv" },
            new { operation = "upload", container = "archive", path = "2026/a.csv", createContainer = true });
        Assert.True(copied.Status == PipeExecutionStatus.Success, copied.Error);
        Assert.Equal("hello", Encoding.UTF8.GetString(setup.Storage.Blobs[("archive", "2026/a.csv")].Content));
        Assert.Contains("create archive", setup.Storage.Log);

        var noContainer = await setup.RunAsync(
            new { operation = "download", container = "in", path = "a.csv" },
            new { operation = "upload", container = "nowhere", path = "a.csv" });
        Assert.Contains("Create container", noContainer.Error);

        var exists = await setup.RunAsync(
            new { operation = "download", container = "in", path = "a.csv" },
            new { operation = "upload", container = "out", path = "taken.csv" });
        Assert.Contains("Overwrite", exists.Error);
        Assert.Equal("old", Encoding.UTF8.GetString(setup.Storage.Blobs[("out", "taken.csv")].Content));
    }

    [Fact]
    public async Task Download_refuses_a_blob_that_is_too_large_or_missing()
    {
        using var setup = await NewAsync(s => s.Add("in", "big.bin", new string('x', 2 * 1024 * 1024)));

        var tooLarge = await setup.RunAsync(new { operation = "download", container = "in", path = "big.bin", maxFileSizeMb = 1 });
        Assert.Contains("more than the allowed 1 MB", tooLarge.Error);

        var missing = await setup.RunAsync(new { operation = "download", container = "in", path = "none.bin" });
        Assert.Contains("not found", missing.Error);
        Assert.DoesNotContain(setup.Storage.Log, l => l.StartsWith("download"));
    }

    [Fact]
    public async Task Rename_copies_into_another_container_and_deletes_the_source()
    {
        using var setup = await NewAsync(s => s.Add("in", "a.csv", "a").Add("done", "x"));

        var moved = await setup.RunAsync(new { operation = "rename", container = "in", path = "a.csv", newContainer = "done", newPath = "2026/a.csv" });

        Assert.True(moved.Status == PipeExecutionStatus.Success, moved.Error);
        var item = moved.Output("blob0").Single();
        Assert.Equal(("done", "2026/a.csv", "in", "a.csv"),
            (item["container"]!.GetValue<string>(), item["path"]!.GetValue<string>(), item["previousContainer"]!.GetValue<string>(), item["previousPath"]!.GetValue<string>()));
        Assert.Empty(setup.Storage.Paths("in"));
        Assert.Equal(["2026/a.csv", "x"], setup.Storage.Paths("done"));
    }

    [Fact]
    public async Task Delete_removes_a_blob_and_a_missing_one_is_an_error()
    {
        using var setup = await NewAsync(s => s.Add("in", "a.csv"));

        var deleted = await setup.RunAsync(new { operation = "delete", container = "in", path = "a.csv" });
        Assert.True(deleted.Output("blob0").Single()["deleted"]!.GetValue<bool>());

        var again = await setup.RunAsync(new { operation = "delete", container = "in", path = "a.csv" });
        Assert.Equal(PipeExecutionStatus.Error, again.Status);
    }

    #endregion

    #region trigger

    private sealed class FakeContext : IPipeTriggerContext
    {
        private readonly AzureBlobTriggerNodeParameters _parameters;
        private readonly PipeCredential _credential = new()
        {
            Id = "storage",
            Name = "Storage",
            TypeId = Guid.Parse(AzureStorageCredential.TypeId),
            Data = PipeJson.ToJsonObject(new AzureStorageCredential { ConnectionString = "UseDevelopmentStorage=true" })
        };

        public FakeContext(AzureBlobTriggerNodeParameters parameters)
        {
            _parameters = parameters;
            _parameters.CredentialId ??= "storage";
        }

        public List<IReadOnlyList<PipeItem>> Fired { get; } = new();
        public bool Busy { get; set; }
        public int LoggedErrors => ((CollectingLogger)Logger).Errors;

        public string WorkflowId => "w";
        public string WorkflowName => "W";
        public string NodeId => "n";
        public string NodeName => "Blob";
        public IPipeTriggerHost Host => throw new NotSupportedException();
        public IReadOnlyCollection<string> SelectableEnvironments => [];
        public ILogger Logger { get; } = new CollectingLogger();

        public TParameters GetParameters<TParameters>() where TParameters : class, new()
            => (_parameters as TParameters) ?? throw new NotSupportedException();

        public Task<PipeCredential> GetCredentialAsync(string credentialId, CancellationToken cancellationToken = default) => Task.FromResult(_credential);

        public Task<PipeExecution?> FireAsync(IReadOnlyList<PipeItem> items, CancellationToken cancellationToken, PipeTriggerFireOptions? options = null)
        {
            if (Busy)
            {
                return Task.FromResult<PipeExecution?>(null);
            }
            Fired.Add(items);
            return Task.FromResult<PipeExecution?>(new PipeExecution { Status = PipeExecutionStatus.Success });
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors++;
            }
        }
    }

    static private (AzureBlobWatcher Watcher, FakeContext Context, FakeStorage Storage) Watch(
        Action<AzureBlobTriggerNodeParameters>? configure = null, Action<FakeStorage>? prepare = null)
    {
        var parameters = new AzureBlobTriggerNodeParameters { Container = "in", Path = "orders", TargetFolder = "done" };
        configure?.Invoke(parameters);
        var storage = new FakeStorage();
        prepare?.Invoke(storage);
        var context = new FakeContext(parameters);
        return (new AzureBlobWatcher(storage, context, parameters), context, storage);
    }

    static private List<string> Names(IReadOnlyList<PipeItem> items) => items.Select(i => i.Json["fileName"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task By_default_every_blob_is_reported_at_once_and_moved_once_the_run_has_started()
    {
        var (watcher, context, storage) = Watch(prepare: s => s.Add("in", "orders/b.csv").Add("in", "orders/a.csv").Add("in", "orders/sub/c.csv"));

        await watcher.CheckAsync(CancellationToken.None);

        var items = Assert.Single(context.Fired);
        Assert.Equal(["a.csv", "b.csv"], Names(items));
        Assert.Equal("created", items[0].Json["event"]!.GetValue<string>());
        Assert.Equal(["done/a.csv", "done/b.csv", "orders/sub/c.csv"], storage.Paths("in"));

        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task Move_can_go_to_another_container_and_a_taken_name_gets_a_number()
    {
        var (watcher, context, storage) = Watch(p => p.TargetContainer = "archive", s => s.Add("in", "orders/a.csv").Add("archive", "done/a.csv", "old"));

        await watcher.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.Equal(["done/a.csv", "done/a_2.csv"], storage.Paths("archive"));
        Assert.Empty(storage.Paths("in"));
    }

    [Fact]
    public async Task Delete_removes_the_blobs_and_the_pattern_is_respected()
    {
        var (watcher, context, storage) = Watch(
            p => { p.AfterProcessing = AzureBlobAfterProcessing.Delete; p.Pattern = "*.csv"; },
            s => s.Add("in", "orders/a.csv").Add("in", "orders/b.txt"));

        await watcher.CheckAsync(CancellationToken.None);

        Assert.Equal(["a.csv"], Names(Assert.Single(context.Fired)));
        Assert.Equal(["orders/b.txt"], storage.Paths("in"));
    }

    [Fact]
    public async Task A_busy_workflow_leaves_the_blobs_for_the_next_check()
    {
        var (watcher, context, storage) = Watch(prepare: s => s.Add("in", "orders/a.csv"));
        context.Busy = true;

        await watcher.CheckAsync(CancellationToken.None);
        Assert.Empty(context.Fired);
        Assert.Equal(["orders/a.csv"], storage.Paths("in"));

        context.Busy = false;
        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task Blobs_that_could_not_be_moved_do_not_start_a_second_run()
    {
        var (watcher, context, storage) = Watch(prepare: s => s.Add("in", "orders/a.csv"));
        storage.FailCopy = true;

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.True(context.LoggedErrors >= 1);
        Assert.Equal(["orders/a.csv"], storage.Paths("in"));
    }

    [Fact]
    public async Task With_Nothing_the_first_check_is_the_baseline_and_changes_only_count_when_asked_for()
    {
        var (created, createdContext, createdStorage) = Watch(p => p.AfterProcessing = AzureBlobAfterProcessing.Nothing, s => s.Add("in", "orders/old.csv"));
        var (changed, changedContext, changedStorage) = Watch(
            p => { p.AfterProcessing = AzureBlobAfterProcessing.Nothing; p.Events = AzureBlobTriggerEvents.CreatedOrChanged; },
            s => s.Add("in", "orders/old.csv"));

        await created.CheckAsync(CancellationToken.None);
        await changed.CheckAsync(CancellationToken.None);
        Assert.Empty(createdContext.Fired);

        createdStorage.Add("in", "orders/new.csv").Add("in", "orders/old.csv", "changed");
        changedStorage.Add("in", "orders/old.csv", "changed");
        await created.CheckAsync(CancellationToken.None);
        await changed.CheckAsync(CancellationToken.None);

        Assert.Equal(["new.csv"], Names(Assert.Single(createdContext.Fired)));
        Assert.Equal("changed", Assert.Single(Assert.Single(changedContext.Fired)).Json["event"]!.GetValue<string>());
        Assert.Equal(["orders/new.csv", "orders/old.csv"], createdStorage.Paths("in"));

        await created.CheckAsync(CancellationToken.None);
        Assert.Single(createdContext.Fired);
    }

    [Fact]
    public async Task Activation_needs_a_credential_a_container_and_a_target_folder_for_moving()
    {
        var node = new AzureBlobTriggerNode(new FakeStorage());

        var noContainer = new AzureBlobTriggerNodeParameters { TargetFolder = "done" };
        await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(new FakeContext(noContainer), CancellationToken.None));

        var noTarget = new AzureBlobTriggerNodeParameters { Container = "in" };
        var error = await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(new FakeContext(noTarget), CancellationToken.None));
        Assert.Contains("target folder", error.Message);

        var noCredential = new AzureBlobTriggerNodeParameters { Container = "in", TargetFolder = "done" };
        var noCredentialContext = new FakeContext(noCredential);
        noCredential.CredentialId = null;
        await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(noCredentialContext, CancellationToken.None));

        var handle = await node.ActivateAsync(new FakeContext(new AzureBlobTriggerNodeParameters { Container = "in", TargetFolder = "done" }), CancellationToken.None);
        Assert.NotNull(handle);
        await handle!.DisposeAsync();
    }

    #endregion
}
