using System.Security.Claims;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// A socket connection <see cref="TraxCompositeSocketInterceptor"/> refused at
/// <c>connection_init</c>. Carries which scheme judged it and why, never the credential.
/// </summary>
/// <param name="Scheme">The token scheme that judged the connection: <c>ApiKey</c>, <c>Jwt</c>, or <c>None</c> when none did (no credential a scheme of a two-scheme host reads, or a host with no token scheme refused by its endpoint policy).</param>
/// <param name="Reason">Why it was refused, one of the <c>Reason*</c> constants.</param>
/// <param name="Principal">The connection's principal when its credential was accepted and the endpoint policy refused it; otherwise <c>null</c>.</param>
internal sealed record SocketRefusal(string Scheme, string Reason, ClaimsPrincipal? Principal)
{
    /// <summary>No credential either registered scheme reads was in the payload.</summary>
    public const string ReasonMissingCredential = "missing credential";

    /// <summary>The scheme rejected the credential.</summary>
    public const string ReasonCredentialRejected = "credential rejected";

    /// <summary>The credential was accepted and the endpoint policy refused its principal.</summary>
    public const string ReasonEndpointPolicy = "endpoint policy";
}

/// <summary>
/// Told of every socket connection refused at <c>connection_init</c>. The audit package registers
/// one, so credential probing over a socket, which never reaches the executor, is recorded.
/// </summary>
internal interface ISocketRefusalObserver
{
    /// <summary>Records <paramref name="refusal"/>. Must not throw or block.</summary>
    void Refused(SocketRefusal refusal);
}
