using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Credentials;
using gView.Pipe.Plugins.Nodes.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class EmailTriggerTests
{
    /// <summary>A mailbox in memory: unread mails by folder, and a log of what the trigger did.</summary>
    private sealed class FakeMailbox : IPipeMailConnector
    {
        public List<(string Folder, PipeMailMessage Message)> Unread { get; } = new();
        public List<string> Log { get; } = new();
        public List<PipeMailQuery> Queries { get; } = new();
        public ImapCredential? Credential { get; private set; }
        public bool FailHandling { get; set; }

        public FakeMailbox Add(uint uid, string subject, string from = "ada@example.com", string folder = "INBOX", params PipeMailAttachment[] attachments)
        {
            Unread.Add((folder, new PipeMailMessage(uid, 7, $"<{uid}@example.com>", [new PipeMailAddress("Ada", from)], [new PipeMailAddress(null, "pipe@example.com")], [],
                subject, new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), "body", "<p>body</p>", attachments, false)));
            return this;
        }

        public Task<IPipeMailSession> ConnectAsync(ImapCredential credential, CancellationToken cancellationToken)
        {
            Credential = credential;
            return Task.FromResult<IPipeMailSession>(new Session(this));
        }

        private sealed class Session(FakeMailbox mailbox) : IPipeMailSession
        {
            public Task<IReadOnlyList<PipeMailMessage>> FetchUnreadAsync(PipeMailQuery query, CancellationToken cancellationToken)
            {
                mailbox.Queries.Add(query);
                IReadOnlyList<PipeMailMessage> result = mailbox.Unread
                    .Where(m => m.Folder == query.Folder
                                && (string.IsNullOrEmpty(query.FromContains) || m.Message.From.Any(a => a.Address.Contains(query.FromContains)))
                                && (string.IsNullOrEmpty(query.SubjectContains) || m.Message.Subject.Contains(query.SubjectContains)))
                    .Select(m => m.Message).OrderBy(m => m.Uid).Take(query.Max).ToList();
                return Task.FromResult(result);
            }

            public Task HandleAsync(string folder, IReadOnlyList<uint> uids, MailAfterProcessing action, string? targetFolder, CancellationToken cancellationToken)
            {
                if (mailbox.FailHandling)
                {
                    throw new PipeException("not allowed");
                }
                mailbox.Log.Add($"{action} {folder} [{string.Join(",", uids)}] {targetFolder}");
                mailbox.Unread.RemoveAll(m => m.Folder == folder && uids.Contains(m.Message.Uid));
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeContext : IPipeTriggerContext
    {
        private readonly EmailTriggerNodeParameters _parameters;
        private readonly PipeCredential _credential;

        public FakeContext(EmailTriggerNodeParameters parameters)
        {
            _parameters = parameters;
            _credential = new PipeCredential
            {
                Id = "mail",
                Name = "Mail",
                TypeId = Guid.Parse(PipeBuiltInCredentials.ImapServer),
                Data = PipeJson.ToJsonObject(new ImapCredential { Host = "imap.example.test", UserName = "pipe", Password = "pw" })
            };
            _parameters.CredentialId = "mail";
        }

        public List<(IReadOnlyList<PipeItem> Items, PipeTriggerFireOptions? Options)> Fired { get; } = new();
        public bool Busy { get; set; }
        public List<string> Errors { get; } = new();

        public string WorkflowId => "w";
        public string WorkflowName => "W";
        public string NodeId => "n";
        public string NodeName => "Mail";
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
            Fired.Add((items, options));
            return Task.FromResult<PipeExecution?>(new PipeExecution { Status = PipeExecutionStatus.Success });
        }

        public int LoggedErrors => ((CollectingLogger)Logger).Errors;
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

    static private (PipeMailPoller Poller, FakeContext Context, FakeMailbox Mailbox) Create(Action<EmailTriggerNodeParameters>? configure = null, Action<FakeMailbox>? prepare = null)
    {
        var parameters = new EmailTriggerNodeParameters();
        configure?.Invoke(parameters);
        var mailbox = new FakeMailbox();
        prepare?.Invoke(mailbox);
        var context = new FakeContext(parameters);
        return (new PipeMailPoller(mailbox, context, parameters), context, mailbox);
    }

    [Fact]
    public async Task Unread_mails_start_one_run_with_one_item_each_and_are_marked_as_read_afterwards()
    {
        var attachment = new PipeMailAttachment("order.csv", "text/csv", Encoding.UTF8.GetBytes("a;b"));
        var (poller, context, mailbox) = Create(prepare: m => m.Add(2, "Second").Add(1, "First", attachments: attachment));

        await poller.CheckAsync(CancellationToken.None);

        var (items, options) = Assert.Single(context.Fired);
        Assert.Equal(["First", "Second"], items.Select(i => i.Json["subject"]!.GetValue<string>()));
        var first = items[0];
        Assert.Equal("Ada", first.Json["from"]!["name"]!.GetValue<string>());
        Assert.Equal("ada@example.com", first.Json["from"]!["address"]!.GetValue<string>());
        Assert.Equal("pipe@example.com", first.Json["to"]![0]!["address"]!.GetValue<string>());
        Assert.Equal("<1@example.com>", first.Json["messageId"]!.GetValue<string>());
        Assert.Equal("body", first.Json["text"]!.GetValue<string>());
        Assert.Equal("order.csv", first.Json["attachments"]![0]!.GetValue<string>());
        Assert.False(first.Json["tooLarge"]!.GetValue<bool>());

        var reference = first.Binary["attachment1"];
        Assert.Equal("order.csv", reference.FileName);
        Assert.Equal("a;b", Encoding.UTF8.GetString(options!.BinaryContent![reference.Id]));

        Assert.Equal(["MarkAsRead INBOX [1,2] "], mailbox.Log);
        Assert.Equal("pipe", mailbox.Credential!.UserName);

        await poller.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
    }

    [Fact]
    public async Task Without_mails_nothing_starts()
    {
        var (poller, context, mailbox) = Create();

        await poller.CheckAsync(CancellationToken.None);

        Assert.Empty(context.Fired);
        Assert.Empty(mailbox.Log);
    }

    [Fact]
    public async Task A_busy_workflow_leaves_the_mails_unread_for_the_next_check()
    {
        var (poller, context, mailbox) = Create(prepare: m => m.Add(1, "First"));
        context.Busy = true;

        await poller.CheckAsync(CancellationToken.None);
        Assert.Empty(context.Fired);
        Assert.Empty(mailbox.Log);

        context.Busy = false;
        await poller.CheckAsync(CancellationToken.None);
        Assert.Single(context.Fired);
        Assert.Single(mailbox.Log);
    }

    [Fact]
    public async Task Mails_that_could_not_be_handled_do_not_start_a_second_run()
    {
        var (poller, context, mailbox) = Create(prepare: m => m.Add(1, "First"));
        mailbox.FailHandling = true;

        await poller.CheckAsync(CancellationToken.None);
        await poller.CheckAsync(CancellationToken.None);

        Assert.Single(context.Fired);
        Assert.Equal(1, context.LoggedErrors);
    }

    [Fact]
    public async Task Move_and_delete_use_the_settings_and_the_filter_goes_to_the_server()
    {
        var (poller, context, mailbox) = Create(
            p =>
            {
                p.Folder = "Orders";
                p.FromContains = "ada";
                p.SubjectContains = "Order";
                p.AfterProcessing = MailAfterProcessing.MoveToFolder;
                p.TargetFolder = "Done";
                p.DownloadAttachments = false;
                p.MaxMessageSizeMb = 2;
            },
            m => m.Add(1, "Order 1", folder: "Orders").Add(2, "Hello", folder: "Orders").Add(3, "Order 3", from: "bob@example.com", folder: "Orders"));

        await poller.CheckAsync(CancellationToken.None);

        var query = Assert.Single(mailbox.Queries);
        Assert.Equal(("Orders", "ada", "Order", 50, 2L * 1024 * 1024, false), (query.Folder, query.FromContains, query.SubjectContains, query.Max, query.MaxMessageBytes, query.DownloadAttachments));
        Assert.Equal(["MoveToFolder Orders [1] Done"], mailbox.Log);
        Assert.Equal("Order 1", Assert.Single(Assert.Single(context.Fired).Items).Json["subject"]!.GetValue<string>());
    }

    [Fact]
    public async Task Activation_needs_a_credential_and_a_target_folder_for_moving()
    {
        var node = new EmailTriggerNode(new FakeMailbox());

        // the fake sets a credential id itself; take it away again
        var parameters = new EmailTriggerNodeParameters();
        var contextWithout = new FakeContext(parameters);
        parameters.CredentialId = null;
        await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(contextWithout, CancellationToken.None));

        var move = new EmailTriggerNodeParameters { AfterProcessing = MailAfterProcessing.MoveToFolder };
        var moveContext = new FakeContext(move);
        var error = await Assert.ThrowsAsync<PipeException>(() => node.ActivateAsync(moveContext, CancellationToken.None));
        Assert.Contains("target folder", error.Message);

        var ok = new EmailTriggerNodeParameters();
        var handle = await node.ActivateAsync(new FakeContext(ok), CancellationToken.None);
        Assert.NotNull(handle);
        await handle!.DisposeAsync();
    }

    /// <summary>A trigger that only reads its credential, to see what a trigger gets (ADR 0033).</summary>
    [PipeNode("5d3f3c6e-3d0b-4f0e-8a64-0e2c1b5a9d11", "Credential Probe Trigger", Category = "Triggers")]
    public sealed class CredentialProbeTrigger : IPipeTriggerNode, IPipeNode
    {
        static public string? Seen { get; set; }

        public async Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
        {
            var credential = await context.GetCredentialAsync("probe", cancellationToken);
            Seen = credential.Data["password"]!.GetValue<string>();
            return null;
        }

        public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
            => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));
    }

    [Fact]
    public async Task A_trigger_gets_the_credential_with_fields_from_a_secret_source()
    {
        Environment.SetEnvironmentVariable("PIPE_SECRET_TRIGGER_TEST_PASSWORD", "from-the-environment");
        using var services = PluginTestHost.Create(options: o => o.PluginAssemblies.Add(typeof(CredentialProbeTrigger).Assembly));
        await services.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "probe",
            Name = "Probe",
            TypeId = Guid.Parse(PipeBuiltInCredentials.Smtp),
            Data = PipeJson.ToJsonObject(new { host = "h", password = "secret:env/trigger-test-password" })
        });
        var workflow = new PipeWorkflowBuilder()
            .Node("trigger", "5d3f3c6e-3d0b-4f0e-8a64-0e2c1b5a9d11")
            .Configure(w => w.Active = true)
            .Build();

        var problems = await services.GetRequiredService<IPipeTriggerManager>().ActivateAsync(workflow);

        Assert.Empty(problems);
        Assert.Equal("from-the-environment", CredentialProbeTrigger.Seen);
    }
}
