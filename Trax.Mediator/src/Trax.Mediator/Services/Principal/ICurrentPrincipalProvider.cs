namespace Trax.Mediator.Services.Principal;

/// <summary>
/// Abstracts "who is the current authenticated caller" for mediator-level
/// services that need it (e.g. per-principal concurrency limits). The mediator
/// itself does not depend on ASP.NET Core; hosts that run inside an HTTP pipeline
/// register an implementation that reads the principal id from an HttpContext.
/// </summary>
/// <remarks>
/// Implementations should be cheap and thread-safe; a call-site may invoke
/// <see cref="GetCurrentPrincipalId"/> on the hot path. Return <c>null</c> when
/// no authenticated principal is available (scheduler, remote worker, anonymous
/// route).
/// </remarks>
public interface ICurrentPrincipalProvider
{
    /// <summary>
    /// The stable identifier of the current authenticated principal, or
    /// <c>null</c> when no principal is present. Mediator-level services use
    /// this as a bucketing key; the exact semantics (claim name, shape) are
    /// the host's responsibility.
    /// </summary>
    string? GetCurrentPrincipalId();
}
