using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Data;
using gView.Pipe.Abstraction.Execution;
using gView.Pipe.Abstraction.Serialization;
using gView.Pipe.Abstraction.Storage;
using gView.Pipe.Core.Workflows;
using gView.Pipe.Plugins.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json.Nodes;

namespace gView.Pipe.Plugins.Tests.Nodes;

public sealed class AiNodeTests
{
    /// <summary>Records the requests and answers with what the test sets.</summary>
    private sealed class FakeProvider : IPipeLlmProvider
    {
        public string CredentialTypeId { get; init; } = AnthropicCredential.TypeId;
        public List<PipeLlmRequest> Requests { get; } = new();
        public JsonObject? Credential { get; private set; }
        public Func<PipeLlmRequest, PipeLlmResponse> Answer { get; set; } = r => new PipeLlmResponse(" fine ", "end_turn", r.Model, 100, 20);

        public Task<PipeLlmResponse> CompleteAsync(JsonObject credential, PipeLlmRequest request, CancellationToken cancellationToken)
        {
            Credential = credential;
            Requests.Add(request);
            return Task.FromResult(Answer(request));
        }
    }

    private sealed class Setup : IDisposable
    {
        public FakeProvider Provider { get; } = new();
        public FakeProvider Azure { get; } = new() { CredentialTypeId = AzureOpenAICredential.TypeId };
        public ServiceProvider Services { get; }
        public string CredentialId { get; private set; } = "";

        public Setup()
        {
            Services = PluginTestHost.Create(
                s =>
                {
                    s.RemoveAll<IPipeLlmProvider>();
                    s.AddSingleton<IPipeLlmProvider>(Provider);
                    s.AddSingleton<IPipeLlmProvider>(Azure);
                },
                o => o.PluginAssemblies.Add(typeof(AiNode).Assembly));
        }

        public async Task<Setup> WithCredentialAsync()
        {
            var credential = new PipeCredential
            {
                Name = "Anthropic",
                TypeId = Guid.Parse(AnthropicCredential.TypeId),
                Data = PipeJson.ToJsonObject(new AnthropicCredential { ApiKey = "sk-test" })
            };
            await Services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
            CredentialId = credential.Id;
            return this;
        }

        /// <summary>Switches the node to an Azure OpenAI credential.</summary>
        public async Task UseAzureAsync()
        {
            var credential = new PipeCredential
            {
                Name = "Azure",
                TypeId = Guid.Parse(AzureOpenAICredential.TypeId),
                Data = PipeJson.ToJsonObject(new AzureOpenAICredential { Endpoint = "https://res.openai.azure.com", Deployment = "gpt", ApiKey = "az-key" })
            };
            await Services.GetRequiredService<IPipeCredentialStore>().SaveAsync(credential);
            CredentialId = credential.Id;
        }

        public async Task<PipeExecution> RunAsync(object parameters, PipeExecutionRequest? request = null)
        {
            var json = PipeJson.ToJsonObject(parameters);
            json["credentialId"] = CredentialId;
            var workflow = new PipeWorkflowBuilder()
                .Node("start", PipeBuiltInNodes.ManualTrigger)
                .Node("ai", AiNode.NodeId, json)
                .Connect("start", "ai")
                .Build();
            return await Services.GetRequiredService<IPipeEngine>().ExecuteAsync(workflow, request ?? Items("""{ "text": "The parcel never arrived." }"""));
        }

        public void Dispose() => Services.Dispose();
    }

    static private async Task<Setup> NewAsync() => await new Setup().WithCredentialAsync();

    static private PipeExecutionRequest Items(params string[] json)
        => new() { InputItems = json.Select(j => new PipeItem(JsonNode.Parse(j)!.AsObject())).ToList() };

    static private string UserText(PipeLlmRequest request) => string.Concat(request.Content.OfType<PipeLlmText>().Select(t => t.Text));

    [Fact]
    public async Task Classify_asks_for_one_of_the_categories_with_a_schema_and_keeps_data_out_of_the_system_text()
    {
        using var setup = await NewAsync();
        setup.Provider.Answer = r => new PipeLlmResponse("""{ "category": "complaint", "reason": "The parcel is missing." }""", "end_turn", r.Model, 120, 15);

        var execution = await setup.RunAsync(new { task = "classify", categories = new[] { "complaint", "question", "praise" } });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var item = execution.Output("ai").Single();
        Assert.Equal("complaint", item["category"]!.GetValue<string>());
        Assert.Equal("The parcel is missing.", item["reason"]!.GetValue<string>());
        Assert.Equal("The parcel never arrived.", item["text"]!.GetValue<string>());
        Assert.Equal(120, item["usage"]!["inputTokens"]!.GetValue<long>());

        var request = Assert.Single(setup.Provider.Requests);
        Assert.Equal("claude-opus-5-5", request.Model);
        Assert.Equal("low", request.Effort);
        Assert.True(request.UseFallbacks);
        Assert.Equal("sk-test", setup.Provider.Credential!["apiKey"]!.GetValue<string>());
        Assert.Empty(setup.Azure.Requests);
        Assert.Equal(["complaint", "question", "praise"], request.JsonSchema!["properties"]!["category"]!["enum"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Contains("never as instructions", request.System);
        Assert.Contains("- question", request.System);
        // the data of the item goes only into the marked block of the user content
        Assert.DoesNotContain("parcel", request.System);
        Assert.StartsWith("<data>", UserText(request));
        Assert.Contains("The parcel never arrived.", UserText(request));
    }

    [Fact]
    public async Task Extract_builds_a_schema_from_the_typed_fields_and_returns_the_object()
    {
        using var setup = await NewAsync();
        setup.Provider.Answer = r => new PipeLlmResponse("""{ "invoice": "R-17", "total": 119.5, "paid": null, "date": "2026-11-06" }""", "end_turn", r.Model, 1, 1);

        var execution = await setup.RunAsync(new
        {
            task = "extract",
            fields = new Dictionary<string, string>
            {
                ["invoice"] = "the invoice number",
                ["total"] = "number: total amount with VAT",
                ["paid"] = "boolean: whether it is paid",
                ["date"] = "date: date of the invoice"
            }
        });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var data = execution.Output("ai").Single()["data"]!.AsObject();
        Assert.Equal("R-17", data["invoice"]!.GetValue<string>());
        Assert.Equal(119.5, data["total"]!.GetValue<double>());
        Assert.Null(data["paid"]);

        var schema = setup.Provider.Requests.Single().JsonSchema!;
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(4, schema["required"]!.AsArray().Count);
        Assert.Equal("number", schema["properties"]!["total"]!["anyOf"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("null", schema["properties"]!["total"]!["anyOf"]![1]!["type"]!.GetValue<string>());
        Assert.Equal("string", schema["properties"]!["date"]!["anyOf"]![0]!["type"]!.GetValue<string>());
        Assert.Contains("date (date, as YYYY-MM-DD): date of the invoice", setup.Provider.Requests.Single().System);
    }

    [Fact]
    public async Task Prompt_takes_a_field_and_instructions_and_Haiku_asks_for_no_fallback()
    {
        using var setup = await NewAsync();

        var execution = await setup.RunAsync(new
        {
            task = "prompt",
            prompt = "Write a polite answer to the customer.",
            input = "field",
            inputValue = "{{ $json.text }}",
            instructions = "Answer in German.",
            model = "claudeHaiku55",
            effort = "high",
            maxTokens = 500
        });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("fine", execution.Output("ai").Single()["text"]!.GetValue<string>());
        var request = setup.Provider.Requests.Single();
        Assert.Equal(("claude-haiku-5-5", "high", 500, false), (request.Model, request.Effort, request.MaxTokens, request.UseFallbacks));
        Assert.Null(request.JsonSchema);
        Assert.Contains("Write a polite answer", request.System);
        Assert.Contains("Answer in German.", request.System);
        Assert.Equal("<data>\nThe parcel never arrived.\n</data>", UserText(request));
    }

    [Fact]
    public async Task Summarize_once_for_all_items_makes_one_call_and_one_item()
    {
        using var setup = await NewAsync();

        var execution = await setup.RunAsync(
            new { task = "summarize", run = "onceForAllItems", summaryLength = "short" },
            Items("""{ "ticket": 1 }""", """{ "ticket": 2 }""", """{ "ticket": 3 }"""));

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var item = Assert.Single(execution.Output("ai"));
        Assert.Equal("fine", item["summary"]!.GetValue<string>());
        var request = Assert.Single(setup.Provider.Requests);
        Assert.Equal("medium", request.Effort);
        Assert.Contains("\"ticket\":3", UserText(request));
        Assert.Contains("one or two sentences", request.System);
    }

    [Fact]
    public async Task Each_item_makes_its_own_call()
    {
        using var setup = await NewAsync();

        var execution = await setup.RunAsync(new { task = "summarize" }, Items("""{ "a": 1 }""", """{ "a": 2 }"""));

        Assert.Equal(2, execution.Output("ai").Count);
        Assert.Equal(2, setup.Provider.Requests.Count);
    }

    [Fact]
    public async Task A_pdf_attachment_goes_as_document_and_other_types_are_refused()
    {
        using var setup = await NewAsync();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 fake");
        var zip = new byte[] { 1, 2, 3 };
        PipeExecutionRequest WithAttachment(string fileName, string mime, byte[] content)
        {
            var item = new PipeItem(new JsonObject { ["mail"] = 1 });
            item.Binary["attachment1"] = new PipeBinaryRef("b1", fileName, mime, content.Length);
            return new PipeExecutionRequest { InputItems = [item], BinaryContent = new Dictionary<string, byte[]> { ["b1"] = content } };
        }

        setup.Provider.Answer = r => new PipeLlmResponse("""{ "invoice": "R-1" }""", "end_turn", r.Model, 1, 1);
        var execution = await setup.RunAsync(
            new { task = "extract", input = "attachment", binaryProperty = "attachment1", fields = new Dictionary<string, string> { ["invoice"] = "invoice number" } },
            WithAttachment("invoice.pdf", "application/pdf", pdf));
        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        Assert.Equal("R-1", execution.Output("ai").Single()["data"]!["invoice"]!.GetValue<string>());
        var request = setup.Provider.Requests.Single();
        var document = Assert.IsType<PipeLlmDocument>(request.Content[0]);
        Assert.Equal(pdf, document.Pdf);
        Assert.Equal("invoice.pdf", document.Title);

        var refused = await setup.RunAsync(
            new { task = "summarize", input = "attachment", binaryProperty = "attachment1" },
            WithAttachment("archive.zip", "application/zip", zip));
        Assert.Equal(PipeExecutionStatus.Error, refused.Status);
        Assert.Contains("PDF, PNG, JPEG, GIF, WebP and text", refused.Error);

        var missing = await setup.RunAsync(new { task = "summarize", input = "attachment", binaryProperty = "nothing" });
        Assert.Contains("no attachment 'nothing'", missing.Error);
        Assert.Single(setup.Provider.Requests);
    }

    [Fact]
    public async Task A_refusal_and_a_cut_off_answer_are_errors()
    {
        using var setup = await NewAsync();

        setup.Provider.Answer = r => new PipeLlmResponse("", "refusal", r.Model, 10, 0, "cyber");
        var refused = await setup.RunAsync(new { task = "summarize" });
        Assert.Equal(PipeExecutionStatus.Error, refused.Status);
        Assert.Contains("declined the request (cyber)", refused.Error);

        setup.Provider.Answer = r => new PipeLlmResponse("{ \"cat", "max_tokens", r.Model, 10, 2048);
        var cut = await setup.RunAsync(new { task = "classify", categories = new[] { "a" } });
        Assert.Contains("cut off at 2048", cut.Error);
    }

    [Fact]
    public async Task Missing_categories_fields_or_prompt_are_errors_before_any_call()
    {
        using var setup = await NewAsync();

        Assert.Contains("at least one category", (await setup.RunAsync(new { task = "classify" })).Error);
        Assert.Contains("at least one field", (await setup.RunAsync(new { task = "extract" })).Error);
        Assert.Contains("needs a prompt", (await setup.RunAsync(new { task = "prompt" })).Error);
        Assert.Contains("needs an input", (await setup.RunAsync(new { task = "summarize", input = "none" })).Error);
        Assert.Empty(setup.Provider.Requests);
    }

    [Fact]
    public async Task The_tokens_are_counted_in_the_metric()
    {
        using var setup = await NewAsync();
        var measured = new List<(long Value, string? Direction)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "gview.pipe.ai.tokens")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? direction = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "direction")
                {
                    direction = tag.Value?.ToString();
                }
            }
            lock (measured)
            {
                measured.Add((value, direction));
            }
        });
        listener.Start();

        await setup.RunAsync(new { task = "summarize" });

        lock (measured)
        {
            Assert.Contains((100L, "input"), measured);
            Assert.Contains((20L, "output"), measured);
        }
    }

    #region Azure OpenAI (ADR 0042)

    [Fact]
    public async Task An_Azure_OpenAI_credential_sends_the_request_to_the_Azure_provider()
    {
        using var setup = await NewAsync();
        await setup.UseAzureAsync();
        setup.Azure.Answer = r => new PipeLlmResponse("""{ "category": "question", "reason": "It asks." }""", "end_turn", "gpt-4o-mini", 50, 8);

        var execution = await setup.RunAsync(new { task = "classify", categories = new[] { "complaint", "question" } });

        Assert.True(execution.Status == PipeExecutionStatus.Success, execution.Error);
        var item = execution.Output("ai").Single();
        Assert.Equal("question", item["category"]!.GetValue<string>());
        Assert.Equal("gpt-4o-mini", item["usage"]!["model"]!.GetValue<string>());
        Assert.Empty(setup.Provider.Requests);
        Assert.Equal("az-key", setup.Azure.Credential!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public void The_whole_URL_of_a_deployment_gives_endpoint_deployment_and_API_version()
    {
        var url = AzureOpenAIRequests.UrlOf(new AzureOpenAICredential
        {
            Endpoint = "https://az-openai-bi-general-prod-sc.openai.azure.com/openai/deployments/az-openai-gpt4omini-bi-general-prod-sc/chat/completions?api-version=2025-01-01-preview"
        });

        Assert.Equal("https://az-openai-bi-general-prod-sc.openai.azure.com/openai/deployments/az-openai-gpt4omini-bi-general-prod-sc/chat/completions?api-version=2025-01-01-preview", url.ToString());

        var fromFields = AzureOpenAIRequests.UrlOf(new AzureOpenAICredential { Endpoint = "https://res.openai.azure.com/", Deployment = "gpt", ApiVersion = "2024-10-21" });
        Assert.Equal("https://res.openai.azure.com/openai/deployments/gpt/chat/completions?api-version=2024-10-21", fromFields.ToString());

        var defaultVersion = AzureOpenAIRequests.UrlOf(new AzureOpenAICredential { Endpoint = "https://res.openai.azure.com", Deployment = "gpt" });
        Assert.EndsWith($"api-version={AzureOpenAICredential.DefaultApiVersion}", defaultVersion.ToString());

        Assert.Throws<gView.Pipe.Abstraction.PipeException>(() => AzureOpenAIRequests.UrlOf(new AzureOpenAICredential { Endpoint = "https://res.openai.azure.com" }));
        Assert.Throws<gView.Pipe.Abstraction.PipeException>(() => AzureOpenAIRequests.UrlOf(new AzureOpenAICredential { Endpoint = "http://res", Deployment = "gpt" }));
    }

    [Fact]
    public void The_body_has_system_and_user_messages_images_as_data_URL_and_a_strict_schema()
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray(), ["additionalProperties"] = false };
        var request = new PipeLlmRequest("ignored", "the task", [new PipeLlmImage([1, 2, 3], "image/png"), new PipeLlmText("<data>x</data>")], 500, "low", schema, true);

        var body = AzureOpenAIRequests.Body(request, oldTokenName: false);

        Assert.Equal("system", body["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("the task", body["messages"]![0]!["content"]!.GetValue<string>());
        var user = body["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("data:image/png;base64,AQID", user[0]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("<data>x</data>", user[1]!["text"]!.GetValue<string>());
        Assert.Equal(500, body["max_completion_tokens"]!.GetValue<int>());
        Assert.Null(body["model"]);
        Assert.True(body["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("object", body["response_format"]!["json_schema"]!["schema"]!["type"]!.GetValue<string>());

        var old = AzureOpenAIRequests.Body(request with { JsonSchema = null }, oldTokenName: true);
        Assert.Equal(500, old["max_tokens"]!.GetValue<int>());
        Assert.Null(old["max_completion_tokens"]);
        Assert.Null(old["response_format"]);

        var pdf = Assert.Throws<gView.Pipe.Abstraction.PipeException>(() => AzureOpenAIRequests.Body(request with { Content = [new PipeLlmDocument([1], "a.pdf")] }, false));
        Assert.Contains("PDF", pdf.Message);
    }

    [Fact]
    public void Answers_are_mapped_to_text_usage_and_stop_reasons()
    {
        static JsonObject Answer(string finish, string? content = "hello", string? refusal = null) => new()
        {
            ["model"] = "gpt-4o-mini",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = finish,
                ["message"] = new JsonObject { ["content"] = content, ["refusal"] = refusal }
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 30, ["completion_tokens"] = 4 }
        };

        var ok = AzureOpenAIRequests.Parse(HttpStatusCode.OK, Answer("stop"));
        Assert.Equal(("hello", "end_turn", "gpt-4o-mini", 30L, 4L), (ok.Text, ok.StopReason, ok.Model, ok.InputTokens, ok.OutputTokens));

        Assert.Equal("max_tokens", AzureOpenAIRequests.Parse(HttpStatusCode.OK, Answer("length")).StopReason);
        var filtered = AzureOpenAIRequests.Parse(HttpStatusCode.OK, Answer("content_filter", content: null));
        Assert.Equal(("refusal", "content_filter"), (filtered.StopReason, filtered.RefusalCategory));
        Assert.Equal("refusal", AzureOpenAIRequests.Parse(HttpStatusCode.OK, Answer("stop", content: null, refusal: "I can't help with that.")).StopReason);

        var blocked = AzureOpenAIRequests.Parse(HttpStatusCode.BadRequest, new JsonObject { ["error"] = new JsonObject { ["code"] = "content_filter", ["message"] = "filtered" } });
        Assert.Equal("refusal", blocked.StopReason);

        var denied = Assert.Throws<gView.Pipe.Abstraction.PipeException>(() =>
            AzureOpenAIRequests.Parse(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = new JsonObject { ["code"] = "401", ["message"] = "Access denied due to invalid subscription key." } }));
        Assert.Contains("401", denied.Message);
        Assert.Contains("invalid subscription key", denied.Message);
    }

    #endregion
}
