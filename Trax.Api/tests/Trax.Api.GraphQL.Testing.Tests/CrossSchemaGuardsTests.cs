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

    #region ExtensionResolversDeclareParentRequirements

    private const string EdgeReadingFk = """
        [ExtendObjectType(typeof(Article))]
        public sealed class ArticleToBillEdge
        {
            public async Task<BillReference?> GetBill(
                [Parent] Article article,
                CrossSchemaLoader<LegidexContext, Bill> bills,
                CancellationToken ct) => await bills.LoadAsync(article.BillId, ct);
        }
        """;

    private const string EdgeDeclaringFk = """
        [ExtendObjectType(typeof(Article))]
        public sealed class ArticleToBillEdge
        {
            public async Task<BillReference?> GetBill(
                [Parent(requires: nameof(Article.BillId))] Article article,
                CrossSchemaLoader<LegidexContext, Bill> bills,
                CancellationToken ct) => await bills.LoadAsync(article.BillId, ct);
        }
        """;

    [Test]
    public void ExtensionResolvers_ReadingUndeclaredProperty_IsOffender()
    {
        using var repo = new TempRepo().Write("libs/Edges/ArticleToBillEdge.cs", EdgeReadingFk);

        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle().Which.Should().Contain("article.BillId");
    }

    [Test]
    public void ExtensionResolvers_DeclaringTheProperty_Passes()
    {
        using var repo = new TempRepo().Write("libs/Edges/ArticleToBillEdge.cs", EdgeDeclaringFk);

        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(1);
    }

    [Test]
    public void ExtensionResolvers_ReadingOnlyTheKey_Passes()
    {
        // Trax adds the entity key to the projection for hand-written resolvers, so a
        // resolver batching on Id needs no annotation.
        using var repo = new TempRepo().Write(
            "libs/Content/IssueContentExtension.cs",
            """
            [ExtendObjectType(typeof(Issue))]
            public sealed class IssueContentExtension
            {
                public Task<IReadOnlyList<Item>> GetContent(
                    [Parent] Issue issue,
                    IssueContentLoader loader,
                    CancellationToken ct) => loader.LoadAsync(issue.Id, ct);
            }
            """
        );

        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void ExtensionResolvers_MethodCallOnParent_IsNotAPropertyRead()
    {
        using var repo = new TempRepo().Write(
            "libs/Edges/CallEdge.cs",
            """
            [ExtendObjectType(typeof(Article))]
            public sealed class CallEdge
            {
                public string GetLabel([Parent] Article article) => article.ToString();
            }
            """
        );

        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void ExtensionResolvers_FileWithoutExtendObjectType_IsNotInspected()
    {
        using var repo = new TempRepo().Write(
            "libs/Plain.cs",
            "public sealed class Plain { public int Read(Thing t) => t.SomeColumn; }"
        );

        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Passed.Should().BeTrue();
        result.Inspected.Should().Be(0);
    }

    [Test]
    public void ExtensionResolvers_NullOptions_Throws()
    {
        var act = () => CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
