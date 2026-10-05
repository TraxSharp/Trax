namespace Trax.Samples.Bookworm.Auth;

/// <summary>
/// Plaintext demonstration API keys. NO WARRANTY: these are not secrets and every key carries the
/// <c>-key-do-not-use-in-production</c> marker so it can never be mistaken for a real credential
/// (a meta-test enforces the marker).
/// </summary>
public static class ApiKeyDefaults
{
    /// <summary>Acts as the member Ada Reader (principal id <c>TraxApiKey:member</c>).</summary>
    public const string MemberKey = "member-key-do-not-use-in-production";

    /// <summary>Acts as a second member, Grace Hopper (principal id <c>TraxApiKey:other-member</c>).</summary>
    public const string OtherMemberKey = "other-member-key-do-not-use-in-production";

    /// <summary>A librarian: reads every member and loan, and is not a member.</summary>
    public const string LibrarianKey = "librarian-key-do-not-use-in-production";
}
