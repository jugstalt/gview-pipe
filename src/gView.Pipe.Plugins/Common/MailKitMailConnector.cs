using gView.Pipe.Abstraction;
using gView.Pipe.Plugins.Credentials;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace gView.Pipe.Plugins.Common;

/// <summary>The MailKit side of the Email Trigger (ADR 0038): a thin adapter that is not tested against a real server.</summary>
internal sealed class MailKitMailConnector : IPipeMailConnector
{
    public async Task<IPipeMailSession> ConnectAsync(ImapCredential credential, CancellationToken cancellationToken)
    {
        var client = new ImapClient();
        try
        {
            // the certificate of the server is checked; there is no option to skip that
            await client.ConnectAsync(credential.Host, credential.Port, ToSocketOptions(credential.Security), cancellationToken);
            await client.AuthenticateAsync(credential.UserName, credential.Password, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            // the message of the library, never the password
            throw new PipeException($"The connection to {credential.Host}:{credential.Port} failed ({ex.GetType().Name}: {ex.Message})");
        }
        return new Session(client);
    }

    static private SecureSocketOptions ToSocketOptions(ImapSecurity security) => security switch
    {
        ImapSecurity.StartTls => SecureSocketOptions.StartTls,
        ImapSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.SslOnConnect
    };

    private sealed class Session : IPipeMailSession
    {
        private readonly ImapClient _client;

        public Session(ImapClient client)
        {
            _client = client;
        }

        public async Task<IReadOnlyList<PipeMailMessage>> FetchUnreadAsync(PipeMailQuery query, CancellationToken cancellationToken)
        {
            var folder = await OpenAsync(query.Folder, cancellationToken);

            var search = SearchQuery.NotSeen;
            if (!string.IsNullOrWhiteSpace(query.FromContains))
            {
                search = search.And(SearchQuery.FromContains(query.FromContains.Trim()));
            }
            if (!string.IsNullOrWhiteSpace(query.SubjectContains))
            {
                search = search.And(SearchQuery.SubjectContains(query.SubjectContains.Trim()));
            }

            var found = (await folder.SearchAsync(search, cancellationToken)).OrderBy(u => u.Id).Take(Math.Max(1, query.Max)).ToList();
            if (found.Count == 0)
            {
                return [];
            }

            var summaries = await folder.FetchAsync(found, MessageSummaryItems.UniqueId | MessageSummaryItems.Size | MessageSummaryItems.Envelope, cancellationToken);
            var result = new List<PipeMailMessage>(summaries.Count);
            foreach (var summary in summaries.OrderBy(s => s.UniqueId.Id))
            {
                if ((summary.Size ?? 0) > query.MaxMessageBytes)
                {
                    result.Add(FromEnvelope(summary, folder.UidValidity));
                    continue;
                }

                var message = await folder.GetMessageAsync(summary.UniqueId, cancellationToken);
                result.Add(await FromMessageAsync(summary.UniqueId.Id, folder.UidValidity, message, query.DownloadAttachments, cancellationToken));
            }
            return result;
        }

        public async Task HandleAsync(string folderName, IReadOnlyList<uint> uids, MailAfterProcessing action, string? targetFolder, CancellationToken cancellationToken)
        {
            var folder = await OpenAsync(folderName, cancellationToken);
            var ids = uids.Select(u => new UniqueId(u)).ToList();

            switch (action)
            {
                case MailAfterProcessing.MoveToFolder:
                    if (string.IsNullOrWhiteSpace(targetFolder))
                    {
                        throw new PipeException("Move to folder needs a target folder");
                    }
                    await folder.MoveToAsync(ids, await TargetAsync(targetFolder.Trim(), cancellationToken), cancellationToken);
                    break;

                case MailAfterProcessing.Delete:
                    await folder.AddFlagsAsync(ids, MessageFlags.Deleted, true, cancellationToken);
                    if (_client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                    {
                        await folder.ExpungeAsync(ids, cancellationToken);
                    }
                    else
                    {
                        await folder.ExpungeAsync(cancellationToken);
                    }
                    break;

                default:
                    await folder.AddFlagsAsync(ids, MessageFlags.Seen, true, cancellationToken);
                    break;
            }
        }

        private async Task<IMailFolder> OpenAsync(string name, CancellationToken cancellationToken)
        {
            var folder = string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase)
                ? _client.Inbox
                : await _client.GetFolderAsync(name, cancellationToken);
            if (!folder.IsOpen || folder.Access != FolderAccess.ReadWrite)
            {
                await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            }
            return folder;
        }

        private async Task<IMailFolder> TargetAsync(string name, CancellationToken cancellationToken)
        {
            try
            {
                return await _client.GetFolderAsync(name, cancellationToken);
            }
            catch (FolderNotFoundException)
            {
                var root = await _client.GetFolderAsync(_client.PersonalNamespaces[0].Path, cancellationToken);
                return await root.CreateAsync(name, true, cancellationToken);
            }
        }

        static private PipeMailMessage FromEnvelope(IMessageSummary summary, uint uidValidity)
        {
            var envelope = summary.Envelope;
            return new PipeMailMessage(
                summary.UniqueId.Id, uidValidity, envelope?.MessageId,
                Addresses(envelope?.From), Addresses(envelope?.To), Addresses(envelope?.Cc),
                envelope?.Subject ?? "", envelope?.Date, null, null, [], TooLarge: true);
        }

        static private async Task<PipeMailMessage> FromMessageAsync(uint uid, uint uidValidity, MimeMessage message, bool download, CancellationToken cancellationToken)
        {
            var attachments = new List<PipeMailAttachment>();
            var number = 0;
            foreach (var part in message.Attachments.OfType<MimePart>())
            {
                number++;
                var name = part.FileName ?? $"attachment{number}";
                byte[]? content = null;
                if (download)
                {
                    using var stream = new MemoryStream();
                    await part.Content.DecodeToAsync(stream, cancellationToken);
                    content = stream.ToArray();
                }
                attachments.Add(new PipeMailAttachment(name, part.ContentType.MimeType, content));
            }

            return new PipeMailMessage(
                uid, uidValidity, message.MessageId,
                Addresses(message.From), Addresses(message.To), Addresses(message.Cc),
                message.Subject ?? "", message.Date == DateTimeOffset.MinValue ? null : message.Date,
                message.TextBody, message.HtmlBody, attachments, TooLarge: false);
        }

        static private List<PipeMailAddress> Addresses(IEnumerable<InternetAddress>? addresses)
            => (addresses ?? []).OfType<MailboxAddress>().Select(a => new PipeMailAddress(string.IsNullOrEmpty(a.Name) ? null : a.Name, a.Address)).ToList();

        public ValueTask DisposeAsync()
        {
            if (_client.IsConnected)
            {
                _client.Disconnect(true);
            }
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
