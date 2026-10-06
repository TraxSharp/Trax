using System.Text.Json.Nodes;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Decides which GraphQL request variables, if any, land in an audit entry. Variables carry
/// whatever the caller sent: credentials, tokens, personal data. The default,
/// <see cref="DefaultAuditRedactor"/>, records none of them; a host that wants them recorded
/// registers its own redactor and chooses what to keep.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// The variables arrive as a JSON object built for this call: an input object is a nested
/// <see cref="JsonObject"/> and a list a <see cref="JsonArray"/>, so a field such as
/// <c>$input.password</c> can be found and removed at any depth. The redactor may change the
/// object in place and return it, return a different one, or return <c>null</c> to record no
/// variables. If it throws, the entry is recorded without variables.
/// </para>
/// <para>
/// Literal values written in the document itself are never recorded, whatever the redactor
/// does: the document is stored with every string and number replaced by a placeholder. See
/// <c>docs/adr/0027-an-audit-entry-records-no-value-the-caller-sent-unless-the-host-opts-in.md</c>.
/// </para>
/// </remarks>
public interface ITraxAuditRedactor
{
    /// <summary>
    /// Returns the variables to record, or <c>null</c> to record none.
    /// <paramref name="variables"/> is <c>null</c> when the request had none.
    /// </summary>
    JsonObject? Redact(JsonObject? variables);
}

/// <summary>
/// The redactor <c>AddAudit</c> registers when the host registers none. It records no variables.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// To record variables, register an <see cref="ITraxAuditRedactor"/> of your own that returns
/// the ones you want kept, with anything sensitive removed.
/// </para>
/// </remarks>
public sealed class DefaultAuditRedactor : ITraxAuditRedactor
{
    /// <inheritdoc />
    public JsonObject? Redact(JsonObject? variables) => null;
}
