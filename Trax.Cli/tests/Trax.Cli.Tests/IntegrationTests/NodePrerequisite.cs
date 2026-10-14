namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// What a test that spawns real node does when node, or the engine's npm packages, are missing: it skips on a
/// developer's machine and fails under CI (where <c>CI</c> is set), which installs both for this folder. A
/// test that skipped there would pass while proving nothing.
/// </summary>
internal static class NodePrerequisite
{
    public static void Missing(string why)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            Assert.Fail(
                $"{why} CI installs node and runs `npm ci` in Trax.Api.StateMachine for this folder, so a "
                    + "test that skipped here would prove nothing."
            );
        Assert.Ignore(why);
    }
}
