using gView.Pipe.Abstraction;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Nodes;
using gView.Pipe.Plugins.Credentials;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Nodes.Network;

public sealed class SendEmailNodeParameters
{
    [Required, PipeCredential(PipeBuiltInCredentials.Smtp)]
    [PipeParameter(Label = "SMTP credential", Order = 1, Expressions = false, Description = "The SMTP server and login used to send the mail")]
    public string? CredentialId { get; set; }

    [PipeParameter(Label = "From", Order = 2, Placeholder = "Pipe <pipe@example.com>", Description = "Empty: the credential's default sender")]
    public string? From { get; set; }

    [Required, PipeParameter(Label = "To", Order = 3, Placeholder = "ada@example.com, grace@example.com")]
    public string To { get; set; } = "";

    [PipeParameter(Label = "Cc", Order = 4)]
    public string? Cc { get; set; }

    [PipeParameter(Label = "Bcc", Order = 5)]
    public string? Bcc { get; set; }

    [Required, PipeParameter(Label = "Subject", Order = 6)]
    public string Subject { get; set; } = "";

    [PipeParameter(Label = "Text", Order = 7, Multiline = true)]
    public string? Text { get; set; }

    [PipeParameter(Label = "HTML", Order = 8, Multiline = true, Description = "Sent next to the text, or alone")]
    public string? Html { get; set; }

    [PipeParameter(Label = "Attachments", Order = 9, Expressions = false,
        Description = "Names of the item's attachments to send; * for all")]
    public List<string> Attachments { get; set; } = new();
}

[PipeNode(PipeBuiltInNodes.SendEmail, "Send Email",
    Category = "Network",
    Description = "Sends one email per item over SMTP, optionally with the item's attachments.")]
public sealed class SendEmailNode : IPipeNode<SendEmailNodeParameters>
{
    public async Task<PipeNodeResult> ExecuteAsync(IPipeNodeContext context, CancellationToken cancellationToken)
    {
        var items = context.GetInput();
        var first = context.GetParameters<SendEmailNodeParameters>(items.Count > 0 ? items[0] : null);
        if (string.IsNullOrEmpty(first.CredentialId))
        {
            throw new PipeException("Select an SMTP credential");
        }
        var smtp = await context.GetCredentialAsync<SmtpCredential>(first.CredentialId, cancellationToken);

        // one connection for all mails of this run
        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? "", cancellationToken);
        }

        var output = new List<PipeItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var parameters = i == 0 ? first : context.GetParameters<SendEmailNodeParameters>(items[i], i);
            var message = await CreateMessageAsync(context, parameters, smtp, items[i], cancellationToken);

            await client.SendAsync(message, cancellationToken);
            context.Logger.LogInformation("Sent '{Subject}' to {To}", message.Subject, message.To.ToString());

            var result = items[i].Clone();
            result.Json["sentEmail"] = new JsonObject
            {
                ["messageId"] = message.MessageId,
                ["to"] = message.To.ToString(),
                ["subject"] = message.Subject
            };
            output.Add(result);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
        return PipeNodeResult.FromMain(output);
    }

    static private async Task<MimeMessage> CreateMessageAsync(
        IPipeNodeContext context,
        SendEmailNodeParameters parameters,
        SmtpCredential smtp,
        PipeItem item,
        CancellationToken cancellationToken)
    {
        var from = string.IsNullOrWhiteSpace(parameters.From) ? smtp.From : parameters.From;
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new PipeException("No sender: set From, or a default sender in the SMTP credential");
        }

        var message = new MimeMessage { Subject = parameters.Subject };
        message.From.AddRange(Addresses(from, "From"));
        message.To.AddRange(Addresses(parameters.To, "To"));
        message.Cc.AddRange(Addresses(parameters.Cc, "Cc"));
        message.Bcc.AddRange(Addresses(parameters.Bcc, "Bcc"));
        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
        {
            throw new PipeException("The email has no recipient");
        }

        var body = new BodyBuilder { TextBody = parameters.Text, HtmlBody = parameters.Html };
        foreach (var (name, reference) in SelectAttachments(item, parameters.Attachments))
        {
            var content = new MemoryStream();
            await using (var source = await context.BinaryStore.OpenReadAsync(reference, cancellationToken))
            {
                await source.CopyToAsync(content, cancellationToken);
            }
            content.Position = 0;
            body.Attachments.Add(string.IsNullOrEmpty(reference.FileName) ? name : reference.FileName, content, ContentType.Parse(reference.MimeType));
        }

        message.Body = body.ToMessageBody();
        return message;
    }

    static private IEnumerable<KeyValuePair<string, PipeBinaryRef>> SelectAttachments(PipeItem item, List<string> names)
    {
        var wanted = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (wanted.Contains("*"))
        {
            return item.Binary;
        }

        foreach (var name in wanted.Where(n => !item.Binary.ContainsKey(n)))
        {
            throw new PipeException($"The item has no attachment '{name}'");
        }
        return item.Binary.Where(b => wanted.Contains(b.Key));
    }

    static private InternetAddressList Addresses(string? text, string field)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }
        return InternetAddressList.TryParse(text, out var addresses)
            ? addresses
            : throw new PipeException($"{field}: '{text}' is not a valid list of email addresses");
    }

    static private SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
