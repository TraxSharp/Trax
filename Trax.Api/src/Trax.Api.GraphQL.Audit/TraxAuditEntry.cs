namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Immutable record describing a single executed GraphQL request. Populated by
/// <see cref="TraxGraphQLAuditListener"/> at request completion and handed to
/// an <see cref="ITraxAuditSink"/> via the background batch writer.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// <paramref name="Timestamp"/> reflects the moment the request started, not
/// the moment it was persisted. Audit sinks that care about persist time should
/// add their own column.
/// </para>
/// </remarks>
public sealed record TraxAuditEntry(
    string PrincipalId,
    string? PrincipalType,
    string? OperationName,
    string Document,
    IReadOnlyDictionary<string, object?>? Variables,
    long DurationMs,
    DateTimeOffset Timestamp,
    bool Success,
    string? ErrorText,
    IReadOnlyDictionary<string, string>? Metadata = null
);
