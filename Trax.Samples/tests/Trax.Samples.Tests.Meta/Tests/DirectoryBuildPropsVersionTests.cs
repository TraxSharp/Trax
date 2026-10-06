namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// A local build packs at the sentinel version 1.99.99, never a real one. The release passes the
/// version it cuts with -p:Version, the same for every package, so a real version written into a
/// folder's Directory.Build.props could only produce a local package that looks like a release.
///
/// <para>Enforces <c>Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md")]
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
                $"every Trax folder must have a Directory.Build.props at its root; none found at '{path}'."
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
                "Directory.Build.props <Version> is locked at 1.99.99, the version a local build "
                    + "packs at. The release passes the version it cuts with -p:Version, the same "
                    + "for every package. See "
                    + "Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md."
            );
    }
}
