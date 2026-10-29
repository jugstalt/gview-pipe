namespace gView.Pipe.Abstraction.Users;

/// <summary>The current user. Single-user for now; prepared for authentication later.</summary>
public interface IPipeUserContext
{
    string? UserId { get; }

    string DisplayName { get; }

    bool IsAuthenticated { get; }
}
