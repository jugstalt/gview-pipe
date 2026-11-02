using gView.Pipe.Abstraction.Users;

namespace gView.Pipe.Core.Users;

/// <summary>Everyone may do everything: hosts without a login (ADR 0021).</summary>
internal sealed class PipeFullAccess : IPipeAuthorization
{
    public Task<PipeUserRoles> GetRolesAsync() => Task.FromResult(PipeUserRoles.FullAccess);
}
