namespace Trax.Samples.Auth.Auth;

/// <summary>
/// The roles this sample's gates name. They are <c>const</c> so attributes can use them
/// (<c>[TraxAuthorize(Roles = AuthRoles.Editor)]</c>), and they are compared exactly and
/// case-sensitively against the caller's role claims.
/// </summary>
public static class AuthRoles
{
    /// <summary>Signed-in reader with no special rights. Proves "authenticated" is not "allowed".</summary>
    public const string Reader = "Reader";

    /// <summary>Publishes articles and reads editor notes.</summary>
    public const string Editor = "Editor";

    /// <summary>Reads the audit trail and editor notes.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Uses the <c>operations</c> namespace: executions, health, queueing and cancelling work.</summary>
    public const string Operator = "Operator";
}

/// <summary>Authorization policies this sample registers with <c>AddAuthorization</c>.</summary>
public static class AuthPolicies
{
    /// <summary>
    /// The caller's identity carries <c>email_verified = true</c>. A policy, unlike a role, can test
    /// any claim, so it combines with a role to mean "an editor whose email is verified".
    /// </summary>
    public const string VerifiedEmail = "VerifiedEmail";

    /// <summary>The claim <see cref="VerifiedEmail"/> reads.</summary>
    public const string EmailVerifiedClaim = "email_verified";
}
