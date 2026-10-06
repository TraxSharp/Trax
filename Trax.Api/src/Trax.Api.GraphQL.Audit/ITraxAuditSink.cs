namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Destination for audit entries. Hosts implement this once (e.g. a Postgres
/// sink, a Serilog sink, a CloudWatch sink) and register it via
/// <c>AddAudit&lt;TSink&gt;()</c>. The background writer calls
/// <see cref="WriteAsync"/> with batched entries; implementations should expect
/// batches of 1 to <c>BatchSize</c> entries.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public interface ITraxAuditSink
{
    /// <summary>Persist a batch of audit entries. Called from the background writer thread.</summary>
    Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct);
}
