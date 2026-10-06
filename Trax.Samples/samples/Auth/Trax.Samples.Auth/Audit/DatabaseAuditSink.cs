using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Audit;
using Trax.Samples.Auth.Data;

namespace Trax.Samples.Auth.Audit;

/// <summary>
/// Stores each audited GraphQL request as an <see cref="AuditRecord"/> row and logs one line for
/// it. The audit writer calls this from a background thread in batches, never on the request
/// thread, so a slow sink never slows a request.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for
/// securing systems that use it.
/// </remarks>
public sealed class DatabaseAuditSink(
    IDbContextFactory<NewsroomDbContext> contexts,
    ILogger<DatabaseAuditSink> logger
) : ITraxAuditSink
{
    public async Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        foreach (var entry in batch)
        {
            db.AuditRecords.Add(
                new AuditRecord
                {
                    PrincipalId = entry.PrincipalId,
                    PrincipalType = entry.PrincipalType,
                    OperationName = entry.OperationName,
                    Document = entry.Document,
                    Success = entry.Success,
                    ErrorText = entry.ErrorText,
                    DurationMs = entry.DurationMs,
                    Timestamp = entry.Timestamp.UtcDateTime,
                }
            );

            logger.LogInformation(
                "[audit] principal={Principal} op={Operation} success={Success} duration={DurationMs}ms",
                entry.PrincipalId,
                entry.OperationName ?? "(anonymous operation)",
                entry.Success,
                entry.DurationMs
            );
        }

        await db.SaveChangesAsync(ct);
    }
}
