namespace Trax.Samples.ApiAudit.Auth;

/// <summary>
/// Authorization roles recognized by the ApiAudit sample. Centralizing these
/// as an enum keeps role string names in one place: <c>nameof(AuditRole.User)</c>
/// produces the same literal that authorization attributes expect.
/// </summary>
public enum AuditRole
{
    /// <summary>Grants access to audited trains.</summary>
    User,
}
