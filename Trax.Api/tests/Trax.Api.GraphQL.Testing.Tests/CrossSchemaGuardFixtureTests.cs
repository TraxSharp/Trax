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
        _repo = new TempRepo()
            .Write("src/App/App.csproj", "<Project />")
            .Write(
                "src/App.CrossSchema/Edges/LoanToBookEdge.cs",
                """
                [ExtendObjectType(typeof(Loan))]
                public sealed class LoanToBookEdge
                {
                    public Task<Book?> GetBook(
                        [Parent] Loan loan,
                        CrossSchemaLoader<CatalogContext, Book> books) => books.LoadAsync(loan.Id);
                }
                """
            );

    [Test]
    public void A_scan_that_finds_nothing_fails_naming_its_roots()
    {
        using var empty = new TempRepo().Write("src/App/App.csproj", "<Project />");
        var options = new ArchitectureGuardOptions
        {
            RepoRootOverride = empty.Root,
            SourceScanRoots = ["src"],
        };
        var result = CrossSchemaGuards.EdgeResolversUseLoader(options);

        AssertionException? failure = null;
        using (new NUnit.Framework.Internal.TestExecutionContext.IsolatedContext())
        {
            try
            {
                AssertInspected(
                    result,
                    expected: true,
                    "cross-schema [ExtendObjectType] resolver",
                    nameof(ExpectsCrossSchemaResolvers),
                    options
                );
            }
            catch (AssertionException ex)
            {
                failure = ex;
            }
        }

        failure.Should().NotBeNull("an empty scan must not pass");
        failure!.Message.Should().Contain("[src]").And.Contain(nameof(ExpectsCrossSchemaResolvers));
    }

    [Test]
    public void A_scan_that_finds_nothing_passes_when_the_repo_opts_out()
    {
        var result = new GuardResult([], 0, "");

        AssertInspected(
            result,
            expected: false,
            "cross-schema [ExtendObjectType] resolver",
            nameof(ExpectsCrossSchemaResolvers),
            new ArchitectureGuardOptions()
        );
    }

    [OneTimeTearDown]
    public void Cleanup() => _repo.Dispose();
}
