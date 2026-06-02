using Trax.Core.Testing;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// Runs <see cref="DomainDataLayerGuardFixture"/> as a consumer would: subclass, configure against a
/// deterministic synthetic repo plus the fake contexts, and let NUnit run the inherited guard methods.
/// Exercises the fixture bodies end to end and dogfoods the turnkey path.
/// </summary>
[TestFixture]
public sealed class DomainDataLayerGuardFixtureSelfTest : DomainDataLayerGuardFixture
{
    private const string DerivedContext =
        "using Trax.Effect.Data.Services.DomainContext;\n"
        + "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o)\n"
        + "    : DomainDataContext<CatalogDbContext>(o), ICatalogDbContext\n"
        + "{ protected override string Schema => \"catalog\"; }";

    private TempRepo _repo = null!;

    protected override ArchitectureGuardOptions Options =>
        new() { RepoRootOverride = _repo.Root, SourceScanRoots = ["src"] };

    protected override IReadOnlyList<Type> DomainContexts =>
        [typeof(AlphaContext), typeof(BetaContext)];

    // AlphaContext maps no entities, so it has nothing outstanding against its snapshot; this drives
    // the inherited migration-snapshot guard down a real (non-vacuous) path.
    protected override IReadOnlyList<Type> MigrationContexts => [typeof(AlphaContext)];

    [OneTimeSetUp]
    public void CreateConformingRepo() =>
        _repo = new TempRepo()
            .Write("src/Catalog/CatalogDbContext.cs", DerivedContext)
            .Write("src/Catalog/ICatalogDbContext.cs", "public interface ICatalogDbContext { }");

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
