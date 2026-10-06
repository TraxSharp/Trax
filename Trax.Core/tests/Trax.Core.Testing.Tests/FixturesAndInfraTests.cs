using Trax.Core.Testing;
using Trax.Core.Testing.Guards;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Tests;

/// <summary>
/// Direct coverage for the repo-root resolution and the source-file / repo-convention edge branches
/// the checker tests do not reach. The NUnit base fixtures themselves are covered by the self-test
/// fixtures in <c>SelfTestFixtures.cs</c>.
/// </summary>
[TestFixture]
public class FixturesAndInfraTests
{
    #region RepoRoot

    [Test]
    public void RepoRoot_resolves_to_a_directory_containing_a_solution()
    {
        var root = RepoRoot.Path;

        Directory
            .EnumerateFiles(root, "*.slnx")
            .Should()
            .NotBeEmpty("RepoRoot walks up to the directory holding the .slnx");
    }

    [Test]
    public void RepoRoot_Combine_and_Relative_round_trip()
    {
        var absolute = RepoRoot.Combine("src", "Example.cs");

        RepoRoot.Relative(absolute).Replace('\\', '/').Should().Be("src/Example.cs");
    }

    #endregion

    #region SourceFiles

    [Test]
    public void SourceFiles_excludes_build_output()
    {
        using var repo = new TempRepo()
            .Write("src/A.cs", "// a")
            .Write("src/bin/B.cs", "// b")
            .Write("src/obj/C.cs", "// c");

        var names = SourceFiles.CSharpUnder(repo.Root, "src").Select(Path.GetFileName).ToList();

        names.Should().Contain("A.cs");
        names.Should().NotContain("B.cs");
        names.Should().NotContain("C.cs");
    }

    #endregion

    #region RepoConventionGuards edge branches

    [Test]
    public void DirectoryBuildPropsVersion_flags_a_missing_file()
    {
        using var repo = new TempRepo();

        var result = RepoConventionGuards.DirectoryBuildPropsVersion(
            new() { RepoRootOverride = repo.Root }
        );

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("missing"));
    }

    [Test]
    public void CrossRepoPackageVersions_flags_an_unparseable_project()
    {
        using var repo = new TempRepo().Write("src/X/X.csproj", "<Project><not-closed");

        var result = RepoConventionGuards.CrossRepoPackageVersions(
            new() { RepoRootOverride = repo.Root }
        );

        result.Offenders.Should().Contain(o => o.Contains("could not parse"));
    }

    #endregion
}
