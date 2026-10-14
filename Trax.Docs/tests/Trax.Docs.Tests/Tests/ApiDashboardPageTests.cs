namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Facts about the API dashboard's stored credential that a host needs to secure it and that the
/// page once stated wrongly: signing out of the shell does not remove the stored key, because the
/// posture refuses the next load before any app code runs, so the host clears it on its own
/// sign-out path; and the key's name, which a sign-out page needs to remove only the dashboard's
/// keys.
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
    public void Signing_out_of_the_shell_is_not_said_to_drop_the_key()
    {
        Flat()
            .Should()
            .NotContain(
                "drops the key",
                "after a sign-out the posture refuses the next load, so no app code runs to drop it"
            )
            .And.Contain("**Signing out of the shell does not remove the stored key.**")
            .And.Contain(
                "the posture refuses the next request for the dashboard, so no app code runs"
            );
    }

    [Test]
    public void The_host_is_told_to_clear_the_key_on_its_sign_out_path()
    {
        Flat()
            .Should()
            .Contain("Clear it on the host's own sign-out path")
            .And.Contain("`Clear-Site-Data: \"storage\"`")
            .And.Contain("removes all of the origin's storage, not only the dashboard's")
            .And.Contain("start with `trax-dashboard-credential:`");
    }
}
