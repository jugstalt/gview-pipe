namespace gView.Pipe.Plugins;

/// <summary>Ids of the built-in node types. Stored in workflows — never change them.</summary>
static public class PipeBuiltInNodes
{
    public const string ManualTrigger = "3f6c1a52-9d0e-4b7a-8c11-0a5e2f7d9b01";
    public const string CronTrigger = "8b2d4e61-1c3f-4a9b-9e27-5d6f7a8b9c02";
    public const string WebhookTrigger = "f1a2b3c4-d5e6-4f7a-8b9c-0d1e2f3a4b12";
    public const string HttpRequest = "c4e8f2a3-6b1d-4f5e-a7c9-1e2d3f4a5b03";
    public const string Set = "5a9b3c7d-2e4f-4a6b-8c1d-9e0f1a2b3c04";
    public const string If = "e1f2a3b4-5c6d-4e7f-8a9b-0c1d2e3f4a05";
    public const string Switch = "7c8d9e0f-1a2b-4c3d-9e4f-5a6b7c8d9e06";
    public const string Merge = "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d07";
    public const string Filter = "9d0e1f2a-3b4c-4d5e-8f6a-7b8c9d0e1f08";
    public const string NoOp = "4e5f6a7b-8c9d-4e0f-a1b2-c3d4e5f6a709";
    public const string Log = "6f7a8b9c-0d1e-4f2a-b3c4-d5e6f7a8b910";
    public const string Code = "8e9f0a1b-2c3d-4e5f-9a6b-7c8d9e0f1a11";
    public const string Sort = "4dde5db0-ab76-422e-a9f7-ef32d7fb4577";
    public const string Limit = "3b4bb3f2-ba00-4c92-9175-eedd0f0e28e0";
    public const string Aggregate = "4d1b5980-4a4a-4e32-9a57-c19ca053d6f2";
    public const string SplitOut = "9e32aa56-9056-4d5e-81bd-254f36ab5e8e";
    public const string RemoveDuplicates = "66640ff1-7fde-47ee-a2f3-ca0b08a62c9f";
    public const string Wait = "65f9894e-ae0f-4bbb-9b04-6652aabaed61";
}

/// <summary>Ids of the built-in credential types.</summary>
static public class PipeBuiltInCredentials
{
    public const string BasicAuth = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c11";
    public const string BearerToken = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d12";
    public const string ApiKeyHeader = "c3d4e5f6-a7b8-4c9d-8e1f-2a3b4c5d6e13";
    public const string ConnectionString = "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f14";
}
