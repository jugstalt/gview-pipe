using System.Text.Json.Nodes;

namespace gView.Pipe.Abstraction.Credentials;

/// <summary>A stored credential. Nodes reference it by <see cref="Id"/> only.</summary>
public sealed class PipeCredential
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Credential type id (<see cref="PipeCredentialTypeAttribute.Id"/>).</summary>
    public Guid TypeId { get; set; }

    /// <summary>The credential fields (serialized credential type). Encrypted at rest.</summary>
    public JsonObject Data { get; set; } = new();
}

/// <summary>Credential without its data, for listings.</summary>
public sealed record PipeCredentialInfo(string Id, string Name, Guid TypeId);
