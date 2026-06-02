using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

// Fake types backing a valid edge for the self-test (top-level so NUnit does not inspect them as
// nested members of the fixture).
public sealed class FixtureFakeBook
{
    public int Id { get; set; }
}

public sealed class FixtureFakeLoan
{
    public int Id { get; set; }
    public int BookId { get; set; }
}

public sealed class FixtureFakeCatalogContext : DbContext
{
    public DbSet<FixtureFakeBook> Books => Set<FixtureFakeBook>();
}

/// <summary>
/// Runs <see cref="CrossSchemaGuardFixture"/> as a consumer would: subclass, configure with a valid
/// edge manifest and a clean source tree, and let NUnit run the inherited guard methods.
/// </summary>
[TestFixture]
public sealed class CrossSchemaGuardFixtureSelfTest : CrossSchemaGuardFixture
{
    private TempRepo _repo = null!;

    protected override ArchitectureGuardOptions Options =>
        new() { RepoRootOverride = _repo.Root, SourceScanRoots = ["src"] };

    protected override IReadOnlyList<CrossSchemaEdge> Edges =>
        [
            new(
                typeof(FixtureFakeLoan),
                nameof(FixtureFakeLoan.BookId),
                typeof(FixtureFakeBook),
                typeof(FixtureFakeCatalogContext),
                "book"
            ),
        ];

    [OneTimeSetUp]
    public void CreateCleanRepo() =>
        _repo = new TempRepo().Write("src/App/App.csproj", "<Project />");

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
