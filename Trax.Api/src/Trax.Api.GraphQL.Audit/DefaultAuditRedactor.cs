using System.Text.Json.Nodes;

namespace Trax.Api.GraphQL.Audit;

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
