using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Core.Testing.Tests;

[TestFixture]
public class RepoConventionGuardsTests
{
    #region DirectoryBuildPropsVersion

    [Test]
    public void DirectoryBuildPropsVersion_PassesAtExpectedVersion()
    {
        using var repo = new TempRepo().Write(
            "Directory.Build.props",
            "<Project><PropertyGroup><Version>1.99.99</Version></PropertyGroup></Project>"
        );

        RepoConventionGuards
            .DirectoryBuildPropsVersion(new() { RepoRootOverride = repo.Root })
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public void DirectoryBuildPropsVersion_FlagsWrongVersion()
    {
        using var repo = new TempRepo().Write(
            "Directory.Build.props",
            "<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>"
        );

        var result = RepoConventionGuards.DirectoryBuildPropsVersion(
            new() { RepoRootOverride = repo.Root }
        );

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("2.0.0"));
    }

    #endregion

    #region CrossRepoPackageVersions

    [Test]
    public void CrossRepoPackageVersions_PassesWhenCentrallyManaged()
    {
        using var repo = new TempRepo()
            .Write(
                "Directory.Packages.props",
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>"
                    + "<ItemGroup><PackageVersion Include=\"Trax.Effect\" Version=\"1.41.0\" /></ItemGroup></Project>"
            )
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup>"
                    + "<PackageReference Include=\"Trax.Effect\" />"
                    + "<PackageReference Include=\"NUnit\" />"
                    + "</ItemGroup></Project>"
            );

        RepoConventionGuards
            .CrossRepoPackageVersions(new() { RepoRootOverride = repo.Root })
            .Passed.Should()
            .BeTrue(
                "versionless Trax refs with a central PackageVersion pin are correct under CPM"
            );
    }

    [Test]
    public void CrossRepoPackageVersions_FlagsInlineVersionOnTraxReference()
    {
        using var repo = new TempRepo()
            .Write(
                "Directory.Packages.props",
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>"
                    + "<ItemGroup><PackageVersion Include=\"Trax.Effect\" Version=\"1.41.0\" /></ItemGroup></Project>"
            )
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup>"
                    + "<PackageReference Include=\"Trax.Effect\" Version=\"2.0.0\" />"
                    + "</ItemGroup></Project>"
            );

        var result = RepoConventionGuards.CrossRepoPackageVersions(
            new() { RepoRootOverride = repo.Root }
        );

        result.Passed.Should().BeFalse();
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains("Trax.Effect") && o.Contains("2.0.0"));
    }

    [Test]
    public void CrossRepoPackageVersions_FlagsMissingCentralPin()
    {
        using var repo = new TempRepo()
            .Write(
                "Directory.Packages.props",
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>"
                    + "<ItemGroup /></Project>"
            )
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup>"
                    + "<PackageReference Include=\"Trax.Effect\" />"
                    + "</ItemGroup></Project>"
            );

        var result = RepoConventionGuards.CrossRepoPackageVersions(
            new() { RepoRootOverride = repo.Root }
        );

        result.Passed.Should().BeFalse();
        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains("Trax.Effect") && o.Contains("PackageVersion"));
    }

    #endregion
}
