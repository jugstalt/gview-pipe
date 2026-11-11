using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Triggers;
using gView.Pipe.Plugins.Common;
using gView.Pipe.Plugins.Credentials;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Triggers;

public sealed class EmailTriggerNodeParameters
{
    [Required, PipeCredential(PipeBuiltInCredentials.ImapServer)]
    [PipeParameter(Label = "Mail server", Order = 1, Expressions = false, Description = "The mailbox to watch: an IMAP Server credential")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "Folder", Order = 2, Expressions = false, Description = "Mail folder to watch, e.g. INBOX or INBOX/Orders")]
    public string Folder { get; set; } = "INBOX";

    [PipeParameter(Label = "From contains", Order = 3, Expressions = false, Description = "Only mails from a sender with this text, e.g. @shop.example; empty: all")]
    public string? FromContains { get; set; }

    [PipeParameter(Label = "Subject contains", Order = 4, Expressions = false, Description = "Only mails with this text in the subject, e.g. Invoice; empty: all")]
    public string? SubjectContains { get; set; }

    [PipeParameter(Label = "After processing", Order = 5, Expressions = false,
        Description = "What happens to a mail once the run has started")]
    public MailAfterProcessing AfterProcessing { get; set; } = MailAfterProcessing.MarkAsRead;

    [PipeParameter(Label = "Target folder", Order = 6, Expressions = false, ShowWhen = nameof(AfterProcessing), ShowWhenValues = ["moveToFolder"],
        Description = "Folder for processed mails, e.g. Done; created if it doesn't exist")]
    public string? TargetFolder { get; set; }

    [PipeParameter(Label = "Download attachments", Order = 7, Expressions = false,
        Description = "Attach the files of the mail to the item as attachment1, attachment2, …")]
    public bool DownloadAttachments { get; set; } = true;

    [PipeParameter(Label = "Max message size (MB)", Order = 8, Expressions = false, Advanced = true,
        Description = "A larger mail is reported with its header only (tooLarge) and handled like the others")]
    [Range(1, 1024)]
    public int MaxMessageSizeMb { get; set; } = 25;

    [PipeParameter(Label = "Check every (seconds)", Order = 9, Expressions = false, Advanced = true, Description = "How often the mailbox is checked")]
    [Range(10, 86400)]
    public int IntervalSeconds { get; set; } = 60;
}

[PipeNodeExamples]
[PipeNode(PipeBuiltInNodes.EmailTrigger, "Email Trigger",
    Category = "Triggers",
    Description = "Starts the workflow when unread mails are in a mailbox (IMAP), while the workflow is active. "
                  + "Checks at an interval; one run per check with one item per mail: sender, recipients, subject, date, text, html and the attachments as binary data. "
                  + "After the run has started the mails are marked as read, moved or deleted. Mails that are unread when the workflow is activated count.")]
public sealed class EmailTriggerNode : IPipeTriggerNode, IPipeNode<EmailTriggerNodeParameters>
{
    private readonly IPipeMailConnector _connector;

    public EmailTriggerNode(IPipeMailConnector connector)
    {
        _connector = connector;
    }

    public Task<IAsyncDisposable?> ActivateAsync(IPipeTriggerContext context, CancellationToken cancellationToken)
    {
        var parameters = context.GetParameters<EmailTriggerNodeParameters>();
        if (string.IsNullOrWhiteSpace(parameters.CredentialId))
        {
            throw new PipeException("Select the IMAP Server credential");
        }
        if (parameters.AfterProcessing == MailAfterProcessing.MoveToFolder && string.IsNullOrWhiteSpace(parameters.TargetFolder))
        {
            throw new PipeException("Move to folder needs a target folder");
        }

        var poller = new PipeMailPoller(_connector, context, parameters);
        var cancellation = new CancellationTokenSource();
        var loop = RunAsync(poller, TimeSpan.FromSeconds(Math.Clamp(parameters.IntervalSeconds, 10, 86400)), cancellation.Token);
        return Task.FromResult<IAsyncDisposable?>(new Registration(cancellation, loop));
    }

    static private async Task RunAsync(PipeMailPoller poller, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await poller.CheckAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
        => Task.FromResult(PipeNodeResult.FromMain(context.GetInput()));

    private sealed class Registration : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _loop;

        public Registration(CancellationTokenSource cancellation, Task loop)
        {
            _cancellation = cancellation;
            _loop = loop;
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
            _cancellation.Dispose();
        }
    }
}

/// <summary>One check of the mailbox per call; keeps what it has handed on but could not mark (ADR 0038).</summary>
public sealed class PipeMailPoller
{
    /// <summary>At most this many mails per run.</summary>
    public const int MaxPerRun = 50;

    private const int MaxRemembered = 1000;

    private readonly IPipeMailConnector _connector;
    private readonly IPipeTriggerContext _context;
    private readonly EmailTriggerNodeParameters _parameters;

    // mails that started a run but could not be marked, moved or deleted: not started again at the next check
    private readonly HashSet<string> _handedOn = new();

    public PipeMailPoller(IPipeMailConnector connector, IPipeTriggerContext context, EmailTriggerNodeParameters parameters)
    {
        _connector = connector;
        _context = context;
        _parameters = parameters;
    }

    /// <summary>Connects, takes the unread mails, starts a run and handles them. Errors are logged, so the next check goes on.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var credential = await _context.GetCredentialAsync(_parameters.CredentialId!, cancellationToken);
            var imap = credential.Data.Deserialize<ImapCredential>(PipeJson.Options) ?? new ImapCredential();

            await using var session = await _connector.ConnectAsync(imap, cancellationToken);
            var folder = string.IsNullOrWhiteSpace(_parameters.Folder) ? "INBOX" : _parameters.Folder.Trim();
            var query = new PipeMailQuery(
                folder, _parameters.FromContains, _parameters.SubjectContains, MaxPerRun,
                (long)Math.Clamp(_parameters.MaxMessageSizeMb, 1, 1024) * 1024 * 1024, _parameters.DownloadAttachments);

            var messages = (await session.FetchUnreadAsync(query, cancellationToken)).Where(m => !_handedOn.Contains(KeyOf(m))).ToList();
            if (messages.Count == 0)
            {
                return;
            }

            var binaryContent = new Dictionary<string, byte[]>();
            var items = messages.Select(m => ToItem(m, folder, binaryContent)).ToList();

            // null: the workflow was busy and its queue full; the mails stay unread and come with the next check
            if (await _context.FireAsync(items, cancellationToken, new PipeTriggerFireOptions { BinaryContent = binaryContent }) is null)
            {
                return;
            }

            if (_handedOn.Count > MaxRemembered)
            {
                _handedOn.Clear();
            }
            foreach (var message in messages)
            {
                _handedOn.Add(KeyOf(message));
            }

            try
            {
                await session.HandleAsync(folder, messages.Select(m => m.Uid).ToList(), _parameters.AfterProcessing, _parameters.TargetFolder, cancellationToken);
                foreach (var message in messages)
                {
                    _handedOn.Remove(KeyOf(message));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _context.Logger.LogError(ex, "{Count} mail(s) started a run but could not be {Action}; they are not started again by this trigger", messages.Count, _parameters.AfterProcessing);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(ex, "The check of the mailbox failed");
        }
    }

    static private string KeyOf(PipeMailMessage message) => $"{message.UidValidity}:{message.Uid}";

    static private PipeItem ToItem(PipeMailMessage message, string folder, Dictionary<string, byte[]> binaryContent)
    {
        var json = new JsonObject
        {
            ["messageId"] = message.MessageId,
            ["uid"] = message.Uid,
            ["folder"] = folder,
            ["from"] = message.From.Count > 0 ? Address(message.From[0]) : null,
            ["to"] = new JsonArray(message.To.Select(Address).ToArray()),
            ["cc"] = new JsonArray(message.Cc.Select(Address).ToArray()),
            ["subject"] = message.Subject,
            ["date"] = message.Date?.ToString("O"),
            ["text"] = message.Text,
            ["html"] = message.Html,
            ["attachments"] = new JsonArray(message.Attachments.Select(a => (JsonNode?)JsonValue.Create(a.FileName)).ToArray()),
            ["tooLarge"] = message.TooLarge
        };

        var item = new PipeItem(json);
        var number = 0;
        foreach (var attachment in message.Attachments)
        {
            number++;
            if (attachment.Content is null)
            {
                continue;
            }

            var id = Guid.NewGuid().ToString("N");
            item.Binary[$"attachment{number}"] = new PipeBinaryRef(id, attachment.FileName, attachment.MimeType, attachment.Content.Length);
            binaryContent[id] = attachment.Content;
        }
        return item;
    }

    static private JsonNode Address(PipeMailAddress address) => new JsonObject { ["name"] = address.Name, ["address"] = address.Address };
}
