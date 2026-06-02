using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

namespace Trax.Api.GraphQL.Testing.Tests;

[TestFixture]
public class CrossSchemaGuardsTests
{
    private sealed class FakeBook
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    private sealed class FakeLoan
    {
        public int Id { get; set; }
        public int BookId { get; set; }
    }

    private sealed class FakeStringFkLoan
    {
        public int Id { get; set; }
        public string BookId { get; set; } = "";
    }

    private sealed class FakeCatalogContext : DbContext
    {
        public DbSet<FakeBook> Books => Set<FakeBook>();
    }

    #region EdgeManifestIsValid

    [Test]
    public void EdgeManifestIsValid_PassesForWellFormedEdge()
    {
        var edges = new List<CrossSchemaEdge>
        {
            new(
                typeof(FakeLoan),
                nameof(FakeLoan.BookId),
                typeof(FakeBook),
                typeof(FakeCatalogContext),
                "book"
            ),
        };

        CrossSchemaGuards.EdgeManifestIsValid(edges).Passed.Should().BeTrue();
    }

    [Test]
    public void EdgeManifestIsValid_FlagsMissingForeignKey()
    {
        var edges = new List<CrossSchemaEdge>
        {
            new(
                typeof(FakeLoan),
                "NotAProperty",
                typeof(FakeBook),
                typeof(FakeCatalogContext),
                "book"
            ),
        };

        var result = CrossSchemaGuards.EdgeManifestIsValid(edges);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().Contain(o => o.Contains("NotAProperty"));
    }

    [Test]
    public void EdgeManifestIsValid_FlagsNonCamelCaseField()
    {
        var edges = new List<CrossSchemaEdge>
        {
            new(
                typeof(FakeLoan),
                nameof(FakeLoan.BookId),
                typeof(FakeBook),
                typeof(FakeCatalogContext),
                "Book"
            ),
        };

        var result = CrossSchemaGuards.EdgeManifestIsValid(edges);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().Contain(o => o.Contains("camelCase"));
    }

    [Test]
    public void EdgeManifestIsValid_FlagsNonIntegerForeignKey()
    {
        var edges = new List<CrossSchemaEdge>
        {
            new(
                typeof(FakeStringFkLoan),
                nameof(FakeStringFkLoan.BookId),
                typeof(FakeBook),
                typeof(FakeCatalogContext),
                "book"
            ),
        };

        var result = CrossSchemaGuards.EdgeManifestIsValid(edges);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().Contain(o => o.Contains("int foreign key"));
    }

    [Test]
    public void EdgeManifestIsValid_FlagsTargetNotOwnedByContext()
    {
        // FakeCatalogContext exposes only DbSet<FakeBook>, not DbSet<FakeLoan>.
        var edges = new List<CrossSchemaEdge>
        {
            new(
                typeof(FakeBook),
                nameof(FakeBook.Id),
                typeof(FakeLoan),
                typeof(FakeCatalogContext),
                "loan"
            ),
        };

        var result = CrossSchemaGuards.EdgeManifestIsValid(edges);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().Contain(o => o.Contains("must expose DbSet<FakeLoan>"));
    }

    #endregion

    #region AddCrossSchemaLoader

    [Test]
    public void AddCrossSchemaLoader_registers_the_closed_loader()
    {
        var services = new ServiceCollection();

        services.AddCrossSchemaLoader<FakeCatalogContext, FakeBook>();

        services
            .Any(d => d.ServiceType == typeof(CrossSchemaLoader<FakeCatalogContext, FakeBook>))
            .Should()
            .BeTrue();
    }

    #endregion

    #region EdgeResolversUseLoader

    [Test]
    public void EdgeResolversUseLoader_PassesWhenLoaderUsed()
    {
        using var repo = new TempRepo().Write(
            "src/App.CrossSchema/Edges/LoanToBookEdge.cs",
            "[ExtendObjectType(typeof(Loan))] public class E {"
                + " public Task<Book?> GetBook(CrossSchemaLoader<C, Book> b) => null!; }"
        );

        CrossSchemaGuards
            .EdgeResolversUseLoader(
                new() { RepoRootOverride = repo.Root, SourceScanRoots = ["src"] }
            )
            .Passed.Should()
            .BeTrue();
    }

    [Test]
    public void EdgeResolversUseLoader_FlagsResolverBypassingLoader()
    {
        using var repo = new TempRepo().Write(
            "src/App.CrossSchema/Edges/LoanToBookEdge.cs",
            "[ExtendObjectType(typeof(Loan))] public class E {"
                + " public Task<Book?> GetBook(ICatalogDbContext db) => null!; }"
        );

        var result = CrossSchemaGuards.EdgeResolversUseLoader(
            new() { RepoRootOverride = repo.Root, SourceScanRoots = ["src"] }
        );

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("LoanToBookEdge.cs"));
    }

    #endregion
}
