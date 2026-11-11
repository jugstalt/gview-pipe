using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Credentials;

namespace gView.Pipe.Plugins.Common;

public enum MailAfterProcessing
{
    [PipeEnumValue("The mail stays in the folder, marked as read")]
    MarkAsRead,

    [PipeEnumValue("The mail moves into the target folder, e.g. Done")]
    MoveToFolder,

    [PipeEnumValue("The mail is deleted")]
    Delete
}

public sealed record PipeMailAddress(string? Name, string Address);

/// <summary>An attachment of a mail; <see cref="Content"/> is null if attachments are not downloaded.</summary>
public sealed record PipeMailAttachment(string FileName, string MimeType, byte[]? Content);

/// <summary>A mail as the Email Trigger reports it (ADR 0038). Without text and attachments if it was <see cref="TooLarge"/>.</summary>
public sealed record PipeMailMessage(
    uint Uid,
    uint UidValidity,
    string? MessageId,
    IReadOnlyList<PipeMailAddress> From,
    IReadOnlyList<PipeMailAddress> To,
    IReadOnlyList<PipeMailAddress> Cc,
    string Subject,
    DateTimeOffset? Date,
    string? Text,
    string? Html,
    IReadOnlyList<PipeMailAttachment> Attachments,
    bool TooLarge);

public sealed record PipeMailQuery(
    string Folder,
    string? FromContains,
    string? SubjectContains,
    int Max,
    long MaxMessageBytes,
    bool DownloadAttachments);

/// <summary>Opens connections to an IMAP server. The trigger talks only to this, so it is tested without a server.</summary>
public interface IPipeMailConnector
{
    /// <exception cref="gView.Pipe.Abstraction.PipeException">The server can't be reached or refuses the login.</exception>
    Task<IPipeMailSession> ConnectAsync(ImapCredential credential, CancellationToken cancellationToken);
}

public interface IPipeMailSession : IAsyncDisposable
{
    /// <summary>The unread mails of the folder that fit the query, oldest first, at most <see cref="PipeMailQuery.Max"/>.</summary>
    Task<IReadOnlyList<PipeMailMessage>> FetchUnreadAsync(PipeMailQuery query, CancellationToken cancellationToken);

    /// <summary>Marks the mails as read, moves them to <paramref name="targetFolder"/> (created if missing) or deletes them.</summary>
    Task HandleAsync(string folder, IReadOnlyList<uint> uids, MailAfterProcessing action, string? targetFolder, CancellationToken cancellationToken);
}
