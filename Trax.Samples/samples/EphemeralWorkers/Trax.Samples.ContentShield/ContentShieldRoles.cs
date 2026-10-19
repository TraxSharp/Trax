namespace Trax.Samples.ContentShield;

/// <summary>
/// Roles the ContentShield trains require. Anyone may submit content for review and look up a
/// result; only a moderator may notify a user of a violation or pull a moderation report. An
/// operator reads and steers every run through the operations namespace, which the operations
/// console uses.
/// </summary>
public static class ContentShieldRoles
{
    public const string Moderator = "Moderator";

    public const string Operator = "Operator";
}
