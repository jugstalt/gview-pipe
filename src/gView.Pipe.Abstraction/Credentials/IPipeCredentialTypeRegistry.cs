namespace gView.Pipe.Abstraction.Credentials;

public sealed class PipeCredentialTypeDescriptor
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Type DataType { get; init; }
    public required string Source { get; init; }
}

public interface IPipeCredentialTypeRegistry
{
    IReadOnlyCollection<PipeCredentialTypeDescriptor> CredentialTypes { get; }

    PipeCredentialTypeDescriptor? Find(Guid id);
}
