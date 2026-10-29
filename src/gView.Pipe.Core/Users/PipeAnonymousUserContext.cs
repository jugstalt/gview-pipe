using gView.Pipe.Abstraction.Users;

namespace gView.Pipe.Core.Users;

/// <summary>Single-user default until authentication is added.</summary>
internal sealed class PipeAnonymousUserContext : IPipeUserContext
{
    public string? UserId => null;
    public string DisplayName => "Anonymous";
    public bool IsAuthenticated => false;
}
