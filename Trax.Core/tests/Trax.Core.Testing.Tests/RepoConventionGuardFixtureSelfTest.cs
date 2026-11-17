using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;

namespace Trax.Core.Testing.Tests;

[TestFixture]
public sealed class RepoConventionGuardFixtureSelfTest : RepoConventionGuardFixture
{
    private TempRepo _repo = null!;

    protected override ArchitectureGuardOptions Options => new() { RepoRootOverride = _repo.Root };

    [OneTimeSetUp]
    public void CreateConformingRepo() =>
        _repo = new TempRepo()
            .Write(
                "Directory.Build.props",
                "<Project><PropertyGroup><Version>1.99.99</Version></PropertyGroup></Project>"
            )
            .Write(
                "Directory.Packages.props",
                "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>"
                    + "<ItemGroup><PackageVersion Include=\"Trax.Effect\" Version=\"1.41.0\" /></ItemGroup></Project>"
            )
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup><PackageReference Include=\"Trax.Effect\" /></ItemGroup></Project>"
            );

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
