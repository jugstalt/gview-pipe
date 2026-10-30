namespace gView.Pipe.AspNetCore.Webhooks;

public class PipeWebhookEndpointOptions
{
    /// <summary>Route prefix; webhooks are called at <c>{Prefix}/{path}</c>.</summary>
    public string Prefix { get; set; } = "/webhook";

    /// <summary>Larger request bodies are rejected with 413.</summary>
    public long MaxBodyBytes { get; set; } = 10 * 1024 * 1024;
}
