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

public enum SmtpSecurity
{
    /// <summary>TLS on connect for port 465, STARTTLS when the server offers it otherwise.</summary>
    Auto,
    SslOnConnect,
    StartTls,

    /// <summary>Unencrypted; only for local test servers.</summary>
    None
}

[PipeCredentialType(PipeBuiltInCredentials.Smtp, "SMTP", Description = "Mail server for sending emails")]
public sealed class SmtpCredential
{
    [Required, PipeParameter(Label = "Host", Order = 1)]
    public string Host { get; set; } = "";

    [PipeParameter(Label = "Port", Order = 2)]
    public int Port { get; set; } = 587;

    [PipeParameter(Label = "Security", Order = 3)]
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    [PipeParameter(Label = "User name", Order = 4, Description = "Leave empty if the server needs no login")]
    public string? UserName { get; set; }

    [Secret, PipeParameter(Label = "Password", Order = 5)]
    public string? Password { get; set; }

    [PipeParameter(Label = "Default sender", Order = 6, Placeholder = "Pipe <pipe@example.com>")]
    public string? From { get; set; }
}

public enum OAuth2ClientAuthentication
{
    /// <summary>HTTP Basic with client id and secret in the Authorization header.</summary>
    Header,

    /// <summary>client_id and client_secret in the form body.</summary>
    Body
}

[PipeCredentialType(PipeBuiltInCredentials.OAuth2ClientCredentials, "OAuth2 Client Credentials",
    Description = "Fetches an access token from a token endpoint (grant client_credentials) and sends it as a bearer token")]
public sealed class OAuth2ClientCredentialsCredential
{
    [Required, PipeParameter(Label = "Token URL", Order = 1, Placeholder = "https://login.example.com/oauth2/token")]
    public string TokenUrl { get; set; } = "";

    [Required, PipeParameter(Label = "Client id", Order = 2)]
    public string ClientId { get; set; } = "";

    [Required, Secret, PipeParameter(Label = "Client secret", Order = 3)]
    public string ClientSecret { get; set; } = "";

    [PipeParameter(Label = "Scope", Order = 4, Description = "Optional; several scopes separated by spaces")]
    public string? Scope { get; set; }

    [PipeParameter(Label = "Audience", Order = 5, Description = "Optional; sent as the parameter audience, which some providers need")]
    public string? Audience { get; set; }

    [PipeParameter(Label = "Send client credentials", Order = 6, Description = "In the Authorization header (usual) or in the body")]
    public OAuth2ClientAuthentication ClientAuthentication { get; set; } = OAuth2ClientAuthentication.Header;
}

public enum ImapSecurity
{
    /// <summary>TLS on connect, usually port 993.</summary>
    SslOnConnect,

    /// <summary>Starts unencrypted and switches to TLS, usually port 143.</summary>
    StartTls,

    /// <summary>Unencrypted; only for local test servers.</summary>
    None
}

[PipeCredentialType(PipeBuiltInCredentials.ImapServer, "IMAP Server", Description = "Mailbox read by the Email Trigger")]
public sealed class ImapCredential
{
    [Required, PipeParameter(Label = "Host", Order = 1)]
    public string Host { get; set; } = "";

    [PipeParameter(Label = "Port", Order = 2)]
    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    [PipeParameter(Label = "Security", Order = 3, Description = "The certificate of the server is always checked")]
    public ImapSecurity Security { get; set; } = ImapSecurity.SslOnConnect;

    [Required, PipeParameter(Label = "User name", Order = 4)]
    public string UserName { get; set; } = "";

    [Required, Secret, PipeParameter(Label = "Password", Order = 5, Description = "An app password if the provider asks for one")]
    public string Password { get; set; } = "";
}
