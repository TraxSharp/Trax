namespace Trax.Samples.Bookworm.Auth;

/// <summary>
/// Plaintext demonstration API keys. NO WARRANTY: these are not secrets and every key carries the
/// <c>-key-do-not-use-in-production</c> marker so it can never be mistaken for a real credential
/// (a meta-test enforces the marker).
/// </summary>
public static class ApiKeyDefaults
{
    public const string MemberKey = "member-key-do-not-use-in-production";
    public const string LibrarianKey = "librarian-key-do-not-use-in-production";
}
