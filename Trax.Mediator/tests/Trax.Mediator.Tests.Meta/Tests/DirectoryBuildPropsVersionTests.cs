namespace Trax.Mediator.Tests.Meta.Tests;

[TestFixture]
public class DirectoryBuildPropsVersionTests
{
    [Test]
    public void Repo_HasDirectoryBuildProps_AtRoot()
    {
        var path = RepoRoot.Combine("Directory.Build.props");
        File.Exists(path)
            .Should()
            .BeTrue(
                $"every Trax repo must have a Directory.Build.props at the repo root; none found at '{path}'."
            );
    }

    [Test]
    public void DirectoryBuildProps_Version_IsLocalDevSentinel()
    {
        var path = RepoRoot.Combine("Directory.Build.props");
        var doc = XDocument.Load(path);
        var version = doc.Root!.Descendants("Version").FirstOrDefault()?.Value;

        version
            .Should()
            .Be(
                "1.99.99",
                "Directory.Build.props <Version> is locked at 1.99.99 for local development. "
                    + "CI overrides this via -p:Version=<semver> from semantic-release. "
                    + "Changing it breaks the nuget.config local-feed wins-over-nuget.org guarantee. "
                    + "See CLAUDE.md > Versioning Strategy."
            );
    }
}
