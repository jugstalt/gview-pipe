namespace gView.Pipe.Abstraction.Credentials;

/// <summary>
/// Registers a POCO class as a credential type. Its properties are the credential fields;
/// mark sensitive ones with <see cref="SecretAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PipeCredentialTypeAttribute : Attribute
{
    public PipeCredentialTypeAttribute(string id, string name)
    {
        Id = Guid.Parse(id);
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string? Description { get; set; }
}

/// <summary>The field is masked in the UI and never shown after saving.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute
{
}
