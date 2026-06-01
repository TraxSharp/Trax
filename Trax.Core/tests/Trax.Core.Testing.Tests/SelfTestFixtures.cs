using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;

namespace Trax.Core.Testing.Tests;

// Self-tests that run the shipped base fixtures exactly as a consumer would: subclass, configure
// against a deterministic synthetic repo, and let NUnit discover and run the inherited [Test] methods.
// These exercise the fixture bodies end to end and double as the dogfood for the turnkey path.

[TestFixture]
public sealed class HygieneGuardFixtureSelfTest : HygieneGuardFixture
{
    private TempRepo _repo = null!;

    protected override ArchitectureGuardOptions Options =>
        new() { RepoRootOverride = _repo.Root, TestScanRoots = ["tests"] };

    [OneTimeSetUp]
    public void CreateCleanRepo() =>
        _repo = new TempRepo().Write(
            "tests/CleanTests.cs",
            "public class CleanTests { public void A() { result.Should().Be(1); } }"
        );

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}

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
                "src/App/App.csproj",
                "<Project><ItemGroup><PackageReference Include=\"Trax.Effect\" Version=\"1.*\" /></ItemGroup></Project>"
            );

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
