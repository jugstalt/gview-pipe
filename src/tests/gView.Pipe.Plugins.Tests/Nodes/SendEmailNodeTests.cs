using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Workflows;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class SendEmailNodeTests : IAsyncDisposable
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly ServiceProvider _services = PluginTestHost.Create();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        _smtp.Dispose();
    }

    private async Task<PipeExecution> RunAsync(object parameters, IReadOnlyList<PipeItem> input, Dictionary<string, byte[]>? binaries = null)
    {
        await _services.GetRequiredService<IPipeCredentialStore>().SaveAsync(new PipeCredential
        {
            Id = "smtp",
            Name = "Test SMTP",
            TypeId = Guid.Parse(PipeBuiltInCredentials.Smtp),
            Data = new JsonObject { ["host"] = "127.0.0.1", ["port"] = _smtp.Port, ["security"] = "none", ["from"] = "Pipe <pipe@example.com>" }
        });

        var workflow = new PipeWorkflowBuilder()
            .Node("start", PipeBuiltInNodes.ManualTrigger)
            .Node("mail", PipeBuiltInNodes.SendEmail, parameters)
            .Connect("start", "mail")
            .Build();
        return await _services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, new PipeExecutionRequest { InputItems = input, BinaryContent = binaries });
    }

    [Fact]
    public async Task Sends_one_mail_per_item_with_attachments()
    {
        var invoice = new PipeItem(new JsonObject { ["email"] = "ada@example.com", ["name"] = "Ada" })
        {
            Binary = { ["invoice"] = new PipeBinaryRef("bin-1", "invoice.txt", "text/plain", 5) }
        };
        var plain = new PipeItem(new JsonObject { ["email"] = "grace@example.com", ["name"] = "Grace" });

        var execution = await RunAsync(new
        {
            credentialId = "smtp",
            to = "{{ $json.email }}",
            bcc = "audit@example.com",
            subject = "Hello {{ $json.name }}",
            text = "Dear {{ $json.name }}",
            attachments = new[] { "*" }
        }, [invoice, plain], new Dictionary<string, byte[]> { ["bin-1"] = "12345"u8.ToArray() });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal(2, _smtp.Messages.Count);

        var first = _smtp.Messages[0];
        Assert.Equal(["ada@example.com", "audit@example.com"], first.Recipients);
        Assert.Contains("Subject: Hello Ada", first.Data);
        Assert.Contains("From: Pipe <pipe@example.com>", first.Data);
        Assert.Contains("filename=invoice.txt", first.Data);
        Assert.DoesNotContain("audit@example.com", first.Data); // Bcc is not in the headers
        Assert.DoesNotContain("filename=", _smtp.Messages[1].Data);

        Assert.Equal("ada@example.com", execution.Output("mail")[0]["sentEmail"]!["to"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_attachment_and_bad_address_fail_the_node()
    {
        var item = new PipeItem(new JsonObject());

        var missing = await RunAsync(new { credentialId = "smtp", to = "a@example.com", subject = "x", attachments = new[] { "invoice" } }, [item]);
        var bad = await RunAsync(new { credentialId = "smtp", to = "not an address <", subject = "x" }, [item]);

        Assert.Contains("no attachment 'invoice'", missing.NodeRuns["mail"].Error);
        Assert.Contains("not a valid list of email addresses", bad.NodeRuns["mail"].Error);
        Assert.Empty(_smtp.Messages);
    }

    /// <summary>Just enough SMTP for MailKit without TLS or login: records envelope recipients and message data.</summary>
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public FakeSmtpServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<(List<string> Recipients, string Data)> Messages { get; } = new();

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await HandleAsync(client);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task HandleAsync(TcpClient client)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 fake smtp");
            var recipients = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO") || command.StartsWith("HELO"))
                {
                    await writer.WriteLineAsync("250 localhost");
                }
                else if (command.StartsWith("RCPT TO:"))
                {
                    recipients.Add(line[8..].Trim().Trim('<', '>'));
                    await writer.WriteLineAsync("250 OK");
                }
                else if (command == "DATA")
                {
                    await writer.WriteLineAsync("354 go ahead");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.AppendLine(dataLine);
                    }
                    lock (Messages)
                    {
                        Messages.Add((recipients, data.ToString()));
                    }
                    recipients = new List<string>();
                    await writer.WriteLineAsync("250 queued");
                }
                else if (command == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    // MAIL FROM, RSET, NOOP, …
                    await writer.WriteLineAsync("250 OK");
                }
            }
        }
    }
}
