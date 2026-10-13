// The EF Core context the sink example on sdk-reference/api-audit/i-trax-audit-sink.md writes to.
using Microsoft.EntityFrameworkCore;

public sealed class AuditRow(TraxAuditEntry entry)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PrincipalId { get; init; } = entry.PrincipalId;
    public string? OperationName { get; init; } = entry.OperationName;
    public bool Success { get; init; } = entry.Success;
    public DateTimeOffset Timestamp { get; init; } = entry.Timestamp;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();
}
