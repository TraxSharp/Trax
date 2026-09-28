using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

    // A context whose per-user entities are all filtered through the principal, so the inherited
    // owner-scope census runs down a real path and passes.
    protected override IReadOnlyList<IReadOnlyModel> OwnerScopedModels
    {
        get
        {
            var options = new DbContextOptionsBuilder<ScopedContext>()
                .UseNpgsql("Host=localhost;Database=offline_model_only")
                .Options;
            using var context = new ScopedContext(options, new NoPrincipal());
            return [context.Model];
        }
    }

    protected override OwnerScopeCensusOptions OwnerScope =>
        new()
        {
            OwnerType = typeof(Account),
            PrincipalAccessorType = typeof(IOwnerPrincipal),
            NavigationScoped = new Dictionary<Type, string>
            {
                [typeof(Answer)] = "an answer belongs to the poll that holds the account id",
            },
        };

    [OneTimeSetUp]
    public void CreateConformingRepo() =>
        _repo = new TempRepo()
            .Write("src/Catalog/CatalogDbContext.cs", DerivedContext)
            .Write("src/Catalog/ICatalogDbContext.cs", "public interface ICatalogDbContext { }")
            // Query code switching a filter off on shared rows, so the inherited filter-bypass
            // scan runs down a real path and passes.
            .Write(
                "src/Catalog/ArticleArchive.cs",
                "public class ArticleArchive { public DbSet<Article> Articles { get; set; } = null!;\n"
                    + "  public IQueryable<Article> Deleted() => Articles.IgnoreQueryFilters(); }"
            );

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
