namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Sanitizes GraphQL request variables before they land in an audit entry.
/// Queries can include auth tokens, PII, connection strings, and other
/// sensitive payloads; this hook lets hosts scrub or omit them so the audit
/// log doesn't become the leak.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public interface ITraxAuditRedactor
{
    /// <summary>Returns a sanitized copy of the GraphQL variables, or <c>null</c> to omit them entirely.</summary>
    IReadOnlyDictionary<string, object?>? Redact(IReadOnlyDictionary<string, object?>? variables);
}

/// <summary>Passthrough redactor. Variables flow to the audit sink unchanged.</summary>
/// <remarks>
/// NO WARRANTY. Hosts that handle sensitive payloads should replace this with
/// their own <see cref="ITraxAuditRedactor"/>. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class DefaultAuditRedactor : ITraxAuditRedactor
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?>? Redact(
        IReadOnlyDictionary<string, object?>? variables
    ) => variables;
}
