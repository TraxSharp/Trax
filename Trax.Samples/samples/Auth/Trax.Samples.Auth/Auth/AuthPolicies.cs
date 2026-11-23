namespace Trax.Samples.Auth.Auth;

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
