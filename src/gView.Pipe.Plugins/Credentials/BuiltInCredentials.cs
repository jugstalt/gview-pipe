using gView.Pipe.Abstraction.Credentials;
using gView.Pipe.Abstraction.Nodes;
using System.ComponentModel.DataAnnotations;

namespace gView.Pipe.Plugins.Credentials;

[PipeCredentialType(PipeBuiltInCredentials.BasicAuth, "Basic Auth", Description = "HTTP basic authentication")]
public sealed class BasicAuthCredential
{
    [Required, PipeParameter(Label = "User name")]
    public string UserName { get; set; } = "";

    [Secret, PipeParameter(Label = "Password")]
    public string Password { get; set; } = "";
}

[PipeCredentialType(PipeBuiltInCredentials.BearerToken, "Bearer Token", Description = "Authorization: Bearer <token>")]
public sealed class BearerTokenCredential
{
    [Required, Secret, PipeParameter(Label = "Token")]
    public string Token { get; set; } = "";
}

[PipeCredentialType(PipeBuiltInCredentials.ApiKeyHeader, "API Key Header", Description = "Sends an API key in a request header")]
public sealed class ApiKeyHeaderCredential
{
    [Required, PipeParameter(Label = "Header name")]
    public string HeaderName { get; set; } = "X-API-Key";

    [Required, Secret, PipeParameter(Label = "Value")]
    public string Value { get; set; } = "";
}

[PipeCredentialType(PipeBuiltInCredentials.ConnectionString, "Connection String", Description = "Database or service connection string")]
public sealed class ConnectionStringCredential
{
    [PipeParameter(Label = "Provider", Placeholder = "e.g. postgres, sqlserver")]
    public string? Provider { get; set; }

    [Required, Secret, PipeParameter(Label = "Connection string")]
    public string ConnectionString { get; set; } = "";
}
