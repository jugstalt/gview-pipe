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
    public const string SplitInBatches = "113c81c3-d938-4d7a-9f6e-8d15958b5f02";
    public const string SplitOut = "9e32aa56-9056-4d5e-81bd-254f36ab5e8e";
    public const string RemoveDuplicates = "66640ff1-7fde-47ee-a2f3-ca0b08a62c9f";
    public const string Wait = "65f9894e-ae0f-4bbb-9b04-6652aabaed61";
    public const string ExecuteWorkflow = "a02238ee-6819-42b5-8b15-837078d74adc";
    public const string ExecuteWorkflowTrigger = "bd47c3b0-0e64-4234-ae83-1f465875ac34";
    public const string ErrorTrigger = "c102a313-b62c-4465-bf28-139c6b1ebbb0";
    public const string RecoveryTrigger = "f81fb49b-3b42-489f-996e-232798fe087f";
    public const string RespondToWebhook = "5aaad06c-980a-4e67-8c1e-40cf04926333";
    public const string ReadFile = "26912bb1-7982-47e7-b6ae-73937b3e83f9";
    public const string WriteFile = "def4eb54-c93e-40d1-9d27-49dfe8f19b41";
    public const string SendEmail = "fafaca25-a6f7-49bd-a689-c3a7f01b1c7a";
    public const string ExtractFromFile = "949aacbc-db66-4cad-a5f3-e6ce66a046bd";
    public const string ConvertToFile = "6a294702-510c-4788-8e62-e5a4489e4875";
    public const string DateTime = "3776167c-16a8-441b-8c44-318c58f59288";
    public const string Crypto = "0cfe2067-05cc-4d62-9922-e380979ed6f4";
    public const string Compression = "4d279dad-5d3a-488b-80d4-5c95cebb0269";
    public const string FormTrigger = "a6d0c1f4-3b52-4e8a-9c17-72e5b0d84f31";
    public const string FileTrigger = "096772ad-7fb1-41f1-9b34-19c8e2634355";
    public const string Result = "d93e7a58-1f06-4b2c-8a4d-6c0b51e92a7f";
}

/// <summary>Ids of the built-in credential types.</summary>
static public class PipeBuiltInCredentials
{
    public const string BasicAuth = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c11";
    public const string BearerToken = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d12";
    public const string ApiKeyHeader = "c3d4e5f6-a7b8-4c9d-8e1f-2a3b4c5d6e13";
    public const string ConnectionString = "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f14";
    public const string Smtp = "f697d17c-18a2-4ea9-a4a5-81d45f2c387a";
    public const string OAuth2ClientCredentials = "b7b000e5-84d5-4cff-8dd3-d3c15df89457";
}
