using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Plugins.Sftp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class SftpTriggerTests
{
    private sealed class FakeContext : IPipeTriggerContext
    {
        private readonly SftpTriggerNodeParameters _parameters;
        private readonly PipeCredential _credential;

        public FakeContext(SftpTriggerNodeParameters parameters)
        {
            _parameters = parameters;
            _parameters.CredentialId = "sftp";
            _credential = new PipeCredential
            {
                Id = "sftp",
                Name = "Partner",
                TypeId = Guid.Parse(SftpCredential.TypeId),
                Data = PipeJson.ToJsonObject(new SftpCredential { Host = "sftp.example.test", UserName = "pipe", Password = "pw", HostKeyFingerprint = "SHA256:abc" })
            };
        }

        public List<IReadOnlyList<PipeItem>> Fired { get; } = new();
        public bool Busy { get; set; }
        public int LoggedErrors => ((CollectingLogger)Logger).Errors;

        public string WorkflowId => "w";
        public string WorkflowName => "W";
        public string NodeId => "n";
        public string NodeName => "SFTP";
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

    static private (SftpFolderWatcher Watcher, FakeContext Context, SftpNodeTests.FakeServer Server) Create(
        Action<SftpTriggerNodeParameters>? configure = null, Action<SftpNodeTests.FakeServer>? prepare = null)
    {
        var parameters = new SftpTriggerNodeParameters { Path = "/in", TargetFolder = "/done" };
        configure?.Invoke(parameters);
        var server = new SftpNodeTests.FakeServer();
        prepare?.Invoke(server);
        var context = new FakeContext(parameters);
        return (new SftpFolderWatcher(server, context, parameters), context, server);
    }

    static private List<string> Names(IReadOnlyList<PipeItem> items) => items.Select(i => i.Json["fileName"]!.GetValue<string>()).Order().ToList();

    [Fact]
    public async Task By_default_every_file_counts_after_two_equal_checks_and_is_moved_once_the_run_has_started()
    {
        var (watcher, context, server) = Create(prepare: s => s.Add("/in/b.csv", "b").Add("/in/a.csv", "a"));

        await watcher.CheckAsync(CancellationToken.None); // first sight
        Assert.Empty(context.Fired);

        await watcher.CheckAsync(CancellationToken.None); // unchanged since
        var items = Assert.Single(context.Fired);
        Assert.Equal(["a.csv", "b.csv"], Names(items));
        Assert.Equal("/in/a.csv", items.Single(i => i.Json["fileName"]!.GetValue<string>() == "a.csv").Json["path"]!.GetValue<string>());
        Assert.Equal("created", items[0].Json["event"]!.GetValue<string>());
        Assert.False(items[0].Json["isDirectory"]!.GetValue<bool>());
        Assert.Equal(["/done/a.csv", "/done/b.csv"], server.Files.Keys.Order());

        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task Folders_part_and_tmp_files_and_files_outside_the_pattern_are_left_alone()
    {
        var (watcher, context, server) = Create(
            p => p.Pattern = "*.csv",
            s => s.Add("/in/a.csv").Add("/in/b.CSV").Add("/in/c.csv.part").Add("/in/d.tmp").Add("/in/e.txt").Add("/in/sub/f.csv"));

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Equal(["a.csv", "b.CSV"], Names(Assert.Single(context.Fired)));
        Assert.Contains("/in/c.csv.part", server.Files.Keys);
        Assert.Contains("/in/sub/f.csv", server.Files.Keys);
    }

    [Fact]
    public async Task Subfolders_can_be_included()
    {
        var (watcher, context, _) = Create(p => p.IncludeSubfolders = true, s => s.Add("/in/a.csv").Add("/in/sub/b.csv"));

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Equal(["a.csv", "b.csv"], Names(Assert.Single(context.Fired)));
    }

    [Fact]
    public async Task A_file_that_still_grows_waits()
    {
        var (watcher, context, server) = Create(prepare: s => s.Add("/in/big.csv"));

        await watcher.CheckAsync(CancellationToken.None);
        server.Touch("/in/big.csv");
        await watcher.CheckAsync(CancellationToken.None);
        Assert.Empty(context.Fired);

        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task Delete_removes_the_files()
    {
        var (watcher, context, server) = Create(p => p.AfterProcessing = SftpAfterProcessing.Delete, s => s.Add("/in/a.csv"));

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.Empty(server.Files);
    }

    [Fact]
    public async Task A_name_that_is_taken_in_the_target_gets_a_number()
    {
        var (watcher, context, server) = Create(prepare: s => s.Add("/in/a.csv", "new").Add("/done/a.csv", "old"));

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.Equal(["/done/a.csv", "/done/a_2.csv"], server.Files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_busy_workflow_leaves_the_files_for_the_next_check()
    {
        var (watcher, context, server) = Create(prepare: s => s.Add("/in/a.csv"));
        context.Busy = true;

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);
        Assert.Empty(context.Fired);
        Assert.Contains("/in/a.csv", server.Files.Keys);

        context.Busy = false;
        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
        Assert.Contains("/done/a.csv", server.Files.Keys);
    }

    [Fact]
    public async Task Files_that_could_not_be_moved_do_not_start_a_second_run()
    {
        var (watcher, context, server) = Create(prepare: s => s.Add("/in/a.csv"));
        server.FailRename = true;

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.True(context.LoggedErrors >= 1);
        Assert.Contains("/in/a.csv", server.Files.Keys);
    }

    [Fact]
    public async Task With_Nothing_the_files_of_the_first_check_are_the_baseline_and_only_new_ones_are_reported()
    {
        var (watcher, context, server) = Create(p => p.AfterProcessing = SftpAfterProcessing.Nothing, s => s.Add("/in/old.csv"));

        await watcher.CheckAsync(CancellationToken.None); // baseline
        server.Add("/in/new.csv");
        await watcher.CheckAsync(CancellationToken.None); // first sight
        Assert.Empty(context.Fired);
        await watcher.CheckAsync(CancellationToken.None);

        var items = Assert.Single(context.Fired);
        Assert.Equal(["new.csv"], Names(items));
        Assert.Equal(["/in/new.csv", "/in/old.csv"], server.Files.Keys.Order());

        await watcher.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task With_Nothing_changed_files_only_count_when_asked_for()
    {
        var (created, createdContext, createdServer) = Create(p => p.AfterProcessing = SftpAfterProcessing.Nothing, s => s.Add("/in/a.csv"));
        var (changed, changedContext, changedServer) = Create(
            p => { p.AfterProcessing = SftpAfterProcessing.Nothing; p.Events = SftpTriggerEvents.CreatedOrChanged; },
            s => s.Add("/in/a.csv"));

        await created.CheckAsync(CancellationToken.None);
        await changed.CheckAsync(CancellationToken.None);
        createdServer.Touch("/in/a.csv");
        changedServer.Touch("/in/a.csv");
        for (var i = 0; i < 3; i++)
        {
            await created.CheckAsync(CancellationToken.None);
            await changed.CheckAsync(CancellationToken.None);
        }

        Assert.Empty(createdContext.Fired);
        Assert.Equal("changed", Assert.Single(Assert.Single(changedContext.Fired)).Json["event"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_logged_and_the_next_check_goes_on()
    {
        var parameters = new SftpTriggerNodeParameters { Path = "/in", TargetFolder = "/done" };
        var context = new FakeContext(parameters);
        var watcher = new SftpFolderWatcher(new RefusingConnector(), context, parameters);

        await watcher.CheckAsync(CancellationToken.None);
        await watcher.CheckAsync(CancellationToken.None);

        Assert.Empty(context.Fired);
        Assert.Equal(2, context.LoggedErrors);
    }

    private sealed class RefusingConnector : ISftpConnector
    {
        public Task<ISftpConnection> ConnectAsync(SftpCredential credential, TimeSpan timeout, CancellationToken cancellationToken)
            => throw new PipeException("the host key doesn't match");
    }

    [Fact]
    public async Task Activation_needs_a_credential_a_path_and_a_target_folder_for_moving()
    {
        var node = new SftpTriggerNode(new SftpNodeTests.FakeServer());

        var noCredential = new SftpTriggerNodeParameters { Path = "/in", TargetFolder = "/done" };
        var noCredentialContext = new FakeContext(noCredential);
        noCredential.CredentialId = null;
        await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(noCredentialContext, CancellationToken.None));

        var noPath = new SftpTriggerNodeParameters { Path = "", TargetFolder = "/done" };
        await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(new FakeContext(noPath), CancellationToken.None));

        var noTarget = new SftpTriggerNodeParameters { Path = "/in" };
        var error = await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(new FakeContext(noTarget), CancellationToken.None));
        Assert.Contains("target folder", error.Message);

        var ok = new SftpTriggerNodeParameters { Path = "/in", TargetFolder = "/done" };
        var handle = await node.ActivateAsync(new FakeContext(ok), CancellationToken.None);
        Assert.NotNull(handle);
        await handle!.DisposeAsync();
    }
}
