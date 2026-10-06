using Microsoft.Extensions.Options;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Records a socket connection refused at <c>connection_init</c>. Such a connection never
/// reaches the executor, so without this a credential probed over the socket would leave no
/// trace in the audit trail.
/// </summary>
/// <remarks>
/// The entry carries no document, since none was sent, and never the credential: only the scheme
/// that judged it and why it was refused, in <see cref="TraxAuditEntry.ErrorText"/> and
/// <see cref="TraxAuditEntry.Metadata"/>. A connection whose credential was accepted and whose
/// principal the endpoint policy refused is recorded against that principal. See
/// <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>.
/// </remarks>
internal sealed class TraxSocketRefusalAudit(
    TraxAuditChannel channel,
    IOptions<TraxAuditOptions> options,
    TimeProvider timeProvider
) : ISocketRefusalObserver
{
    /// <summary>The code an entry for a refused socket connection carries.</summary>
    internal const string RefusedCode = "TRAX_SOCKET_REFUSED";

    public void Refused(SocketRefusal refusal)
    {
        var principalId = options.Value.DefaultPrincipalId;
        string? principalType = null;
        if (refusal.Principal is { } user && user.TryGetPrincipalId(out var id))
        {
            principalId = id;
            principalType = user.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value;
        }

        channel.TryEnqueue(
            new TraxAuditEntry(
                PrincipalId: principalId,
                PrincipalType: principalType,
                OperationName: null,
                Document: string.Empty,
                Variables: null,
                DurationMs: 0,
                Timestamp: timeProvider.GetUtcNow(),
                Success: false,
                ErrorText: $"{RefusedCode} at connection_init ({refusal.Reason})",
                Metadata: new Dictionary<string, string>
                {
                    ["transport"] = "websocket",
                    ["stage"] = "connection_init",
                    ["scheme"] = refusal.Scheme,
                    ["reason"] = refusal.Reason,
                }
            )
        );
    }
}
