using System.Globalization;
using Microsoft.Extensions.Options;
using Trax.Api.DTOs;
using Trax.Api.Services.BulkOperations;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Records how a bulk operation ended. The request that started it is audited like any other;
/// the batches run after that request has finished, so without this entry the audit trail would
/// say an operator asked for a bulk cancel and never what it did.
/// </summary>
/// <remarks>
/// The entry is recorded against the principal that started the operation and carries the
/// operation's id, kind, status and counts in <see cref="TraxAuditEntry.Metadata"/>. It carries
/// no value the caller sent: not the filter, not the note
/// (<c>docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md</c>).
/// </remarks>
internal sealed class TraxBulkOperationAudit(
    TraxAuditChannel channel,
    IOptions<TraxAuditOptions> options
) : IBulkOperationObserver
{
    /// <summary>The operation name a bulk operation's entry carries.</summary>
    internal const string OperationName = "bulkOperation";

    public void Finished(BulkOperationOutcome outcome)
    {
        var finished = outcome.Status == BulkOperationStatus.Finished;
        channel.TryEnqueue(
            new TraxAuditEntry(
                PrincipalId: outcome.Actor.PrincipalId ?? options.Value.DefaultPrincipalId,
                PrincipalType: outcome.Actor.PrincipalType,
                OperationName: OperationName,
                Document: string.Empty,
                Variables: null,
                DurationMs: (long)(outcome.FinishedAt - outcome.StartedAt).TotalMilliseconds,
                Timestamp: new DateTimeOffset(
                    DateTime.SpecifyKind(outcome.StartedAt, DateTimeKind.Utc)
                ),
                Success: finished,
                ErrorText: finished ? null : $"{outcome.Status}: {outcome.Message}",
                Metadata: new Dictionary<string, string>
                {
                    ["bulkOperation"] = outcome.Id.ToString(),
                    ["kind"] = outcome.Kind.ToString(),
                    ["status"] = outcome.Status.ToString(),
                    ["total"] = outcome.Total.ToString(CultureInfo.InvariantCulture),
                    ["done"] = outcome.Done.ToString(CultureInfo.InvariantCulture),
                    ["failed"] = outcome.Failed.ToString(CultureInfo.InvariantCulture),
                    ["actor"] = outcome.Actor.Display,
                }
            )
        );
    }
}
