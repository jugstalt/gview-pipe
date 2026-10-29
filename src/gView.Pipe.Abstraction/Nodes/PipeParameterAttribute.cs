namespace gView.Pipe.Abstraction.Nodes;

/// <summary>UI hints for a property of a node's parameter class.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeParameterAttribute : Attribute
{
    public string? Label { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public int Order { get; set; }
    public bool Multiline { get; set; }

    /// <summary>Whether the editor offers <c>{{ }}</c> expressions for this field.</summary>
    public bool Expressions { get; set; } = true;
}

/// <summary>
/// Marks a string property that holds the id of a stored credential.
/// Restricts the selectable credentials to the given credential type ids.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PipeCredentialAttribute : Attribute
{
    public PipeCredentialAttribute(params string[] credentialTypeIds)
    {
        CredentialTypeIds = credentialTypeIds.Select(Guid.Parse).ToArray();
    }

    public Guid[] CredentialTypeIds { get; }
}
