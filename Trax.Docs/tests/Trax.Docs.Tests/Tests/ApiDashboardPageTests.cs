namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Facts about the API dashboard's credential that a host relies on to secure it: the app holds
/// it in memory only and never in browser storage, so a page load asks for it again and signing
/// out or closing the tab leaves nothing behind. The page once told hosts to clear a stored key
/// on their own sign-out path; that advice is moot now and must not come back.
///
/// <para>Not ADR-enforcing: it pins page content that was found to overclaim, not a recorded
/// decision.</para>
/// </summary>
[TestFixture]
public class ApiDashboardPageTests
{
    private static string Page() =>
        File.ReadAllText(
                RepoRoot.Combine("sdk-reference", "api-dashboard", "use-trax-api-dashboard.md")
            )
            .Replace("\r\n", "\n");

    private static string Flat() =>
        string.Join(' ', Page().Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries));

    [Test]
    public void The_credential_is_said_to_be_held_in_memory_and_never_stored()
    {
        Flat()
            .Should()
            .Contain("**The credential is held in memory only.**")
            .And.Contain(
                "never writes it to `localStorage`, `sessionStorage`, IndexedDB or a cookie"
            )
            .And.Contain("the operator pastes it again after every page load")
            .And.Contain("Closing the tab, or signing out of the shell, leaves nothing behind");
    }

    [Test]
    public void The_host_is_not_told_to_clear_a_stored_key()
    {
        Flat()
            .Should()
            .NotContain("Clear-Site-Data", "nothing is stored for a sign-out path to clear")
            .And.NotContain("trax-dashboard-credential:")
            .And.NotContain("fingerprint");
    }
}
