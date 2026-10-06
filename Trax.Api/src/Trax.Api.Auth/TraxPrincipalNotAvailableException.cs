namespace Trax.Api.Auth;

/// <summary>
/// Thrown when scoped <see cref="TraxPrincipal"/> is requested from the DI
/// container on an execution path that has no authenticated Trax principal.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// This indicates a configuration or routing mistake: a junction that injects
/// <see cref="TraxPrincipal"/> was instantiated on a code path without
/// authentication (anonymous request, the scheduler, or a background service).
/// Either gate the upstream endpoint with <c>[TraxAuthorize]</c>, or switch to
/// injecting <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/> and
/// checking <see cref="TraxPrincipalExtensions.TryGetTraxPrincipal"/> manually
/// for dual-path junctions.
/// </para>
/// </remarks>
public sealed class TraxPrincipalNotAvailableException : InvalidOperationException
{
    private const string DefaultMessage =
        "No authenticated Trax principal is available on the current execution context. "
        + "Injecting TraxPrincipal requires an authenticated HTTP request. For junctions "
        + "that also run from the scheduler or background services, inject "
        + "IHttpContextAccessor and probe TryGetTraxPrincipal instead.";

    /// <summary>Constructs the exception with the default diagnostic message.</summary>
    public TraxPrincipalNotAvailableException()
        : base(DefaultMessage) { }

    /// <summary>Constructs the exception with a custom message.</summary>
    public TraxPrincipalNotAvailableException(string message)
        : base(message) { }
}
