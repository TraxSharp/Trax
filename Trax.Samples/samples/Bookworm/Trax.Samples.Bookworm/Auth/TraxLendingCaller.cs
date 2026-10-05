using Trax.Api.Auth;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Bookworm.Auth;

/// <summary>
/// Binds the lending domain's <see cref="ILendingCaller"/> over Trax's <see cref="TraxCaller"/>.
/// <see cref="TraxCaller"/> is safe to inject anywhere (it never throws for an anonymous caller) and
/// reads the request's principal on each access, so the lending filters see the caller as it is
/// when a query runs.
/// </summary>
public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;

    public bool IsLibrarian => caller.Principal?.Roles.Contains(BookwormRoles.Librarian) == true;
}
