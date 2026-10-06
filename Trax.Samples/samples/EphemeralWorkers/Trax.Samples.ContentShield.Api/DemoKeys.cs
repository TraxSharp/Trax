namespace Trax.Samples.ContentShield.Api;

/// <summary>
/// Plaintext demonstration API key for the moderator-only mutations. NO WARRANTY: it is published
/// in this repository and is not a secret. Program.cs registers it only in Development, and the
/// <c>do-not-use-in-production</c> marker makes Trax.Api refuse to start with it anywhere else.
/// </summary>
public static class DemoKeys
{
    /// <summary>Holds the <see cref="ContentShieldRoles.Moderator"/> role.</summary>
    public const string ModeratorKey = "contentshield-moderator-key-do-not-use-in-production";
}
