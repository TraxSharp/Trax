using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Audit;

namespace Trax.Samples.ApiAudit.Sinks;

/// <summary>
/// Demo sink that writes each audit batch through <see cref="ILogger"/>. Replace
/// with a real sink (Postgres, CloudWatch, Serilog) in production.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class ConsoleAuditSink(ILogger<ConsoleAuditSink> logger) : ITraxAuditSink
{
    public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        foreach (var entry in batch)
        {
            logger.LogInformation(
                "[audit] {Timestamp:o} principal={Principal} op={Operation} success={Success} duration={DurationMs}ms",
                entry.Timestamp,
                entry.PrincipalId,
                entry.OperationName ?? "(anon)",
                entry.Success,
                entry.DurationMs
            );
            if (!entry.Success && entry.ErrorText is not null)
                logger.LogInformation("[audit error] {Error}", entry.ErrorText);
        }
        return Task.CompletedTask;
    }
}
