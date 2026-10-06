using System.Text.Json.Nodes;

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
/// <para>
/// <paramref name="OperationName"/> is the request's <c>operationName</c> field, or, when the
/// request sent none, the name the document gives its only operation. It is cut to
/// <see cref="TraxAuditOptions.MaxOperationNameLength"/>.
/// </para>
/// <para>
/// <paramref name="Document"/> is the request's document with every string and numeric literal
/// replaced by a placeholder (<c>""</c> or <c>0</c>). <paramref name="Variables"/> is what the
/// registered <see cref="ITraxAuditRedactor"/> returned, which by default is <c>null</c>.
/// <paramref name="ErrorText"/> is each error's code and path (<c>CODE at path</c>, joined with
/// <c>; </c>) unless <see cref="TraxAuditOptions.RecordErrorMessages"/> is set, because an error
/// message can quote what the caller sent.
/// </para>
/// </remarks>
public sealed record TraxAuditEntry(
    string PrincipalId,
    string? PrincipalType,
    string? OperationName,
    string Document,
    JsonObject? Variables,
    long DurationMs,
    DateTimeOffset Timestamp,
    bool Success,
    string? ErrorText,
    IReadOnlyDictionary<string, string>? Metadata = null
);
