using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Trax.Core.Testing;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// The census reads the model, so it cannot see query code that switches a filter off. The source
/// scan can: <c>IgnoreQueryFilters()</c>, or an EF10 named-filter disable that names an owner-scope
/// filter, on a per-user set fails unless the file is allowlisted with a reason.
///
/// <para>Enforces <c>docs/adr/0008-per-user-data-is-filtered-by-its-owner.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0008-per-user-data-is-filtered-by-its-owner.md")]
public class OwnerScopeFilterBypassTests
{
    private const string Context = """
        public sealed class AppDbContext : DbContext
        {
            public DbSet<Note> Notes => Set<Note>();
            public DbSet<Article> Articles => Set<Article>();
        }
        """;

    private static OwnerScopeCensusOptions Census(
        IReadOnlyDictionary<string, string>? allowlist = null
    ) =>
        new()
        {
            OwnerType = typeof(Account),
            PrincipalAccessorType = typeof(IOwnerPrincipal),
            FilterBypassAllowlist = allowlist ?? new Dictionary<string, string>(),
        };

    private static IReadOnlyList<IReadOnlyModel> Models<TContext>()
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=offline_model_only")
            .Options;
        using var context = (TContext)
            Activator.CreateInstance(typeof(TContext), options, new NoPrincipal())!;
        return [context.Model];
    }

    private static GuardResult Scan(
        TempRepo repo,
        IReadOnlyList<IReadOnlyModel>? models = null,
        OwnerScopeCensusOptions? census = null
    ) =>
        DataLayerGuards.OwnerScopeFilterBypasses(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["src"],
            },
            models ?? Models<ScopedContext>(),
            census ?? Census()
        );

    private static TempRepo RepoWith(string resolver) =>
        new TempRepo()
            .Write("src/Data/AppDbContext.cs", Context)
            .Write("src/Api/NoteResolver.cs", resolver);

    [Test]
    public void Flags_IgnoreQueryFilters_on_a_per_user_set()
    {
        using var repo = RepoWith(
            "public class NoteResolver { public IQueryable<Note> All(AppDbContext db) => "
                + "db.Notes.IgnoreQueryFilters(); }"
        );

        var result = Scan(repo);

        result
            .Offenders.Should()
            .ContainSingle(
                "switching every filter off on a per-user set serves every owner's rows "
                    + "(docs/adr/0008-per-user-data-is-filtered-by-its-owner.md)"
            )
            .Which.Should()
            .Contain("src/Api/NoteResolver.cs:1")
            .And.Contain("Note");
    }

    [Test]
    public void Flags_IgnoreQueryFilters_on_a_per_user_set_reached_through_Set()
    {
        using var repo = RepoWith(
            "public class NoteResolver { public IQueryable<Note> All(DbContext db) =>\n"
                + "    db.Set<Note>()\n        .Where(n => n.Id > 0)\n        .IgnoreQueryFilters(); }"
        );

        Scan(repo).Offenders.Should().ContainSingle().Which.Should().Contain("NoteResolver.cs:4");
    }

    [Test]
    public void Flags_IgnoreQueryFilters_on_a_derived_per_user_type()
    {
        using var repo = RepoWith(
            "public class R { public object All(DbContext db) => "
                + "db.Set<PinnedNote>().IgnoreQueryFilters(); }"
        );

        Scan(repo).Offenders.Should().ContainSingle();
    }

    [Test]
    public void Passes_IgnoreQueryFilters_on_a_shared_set()
    {
        using var repo = RepoWith(
            "public class ArticleResolver { public IQueryable<Article> Deleted(AppDbContext db) => "
                + "db.Articles.IgnoreQueryFilters().Where(a => a.DeletedAt != null); }"
        );

        var result = Scan(repo);

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(1);
    }

    [Test]
    public void Flags_IgnoreQueryFilters_whose_set_it_cannot_tell()
    {
        using var repo = RepoWith(
            "public class R { public IQueryable<T> Everything<T>(IQueryable<T> query) "
                + "where T : class => query.IgnoreQueryFilters(); }"
        );

        Scan(repo)
            .Offenders.Should()
            .ContainSingle("a set the scan cannot name may be a per-user one")
            .Which.Should()
            .Contain("cannot tell");
    }

    [Test]
    public void Passes_a_named_disable_of_a_filter_that_is_not_the_owner_scope()
    {
        using var repo = RepoWith(
            "public class R { public object All(AppDbContext db) => "
                + "db.Notes.IgnoreQueryFilters([\"Visible\"]); }"
        );

        var result = Scan(repo, Models<NamedFilterContext>());

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void Flags_a_named_disable_of_the_owner_scope_filter()
    {
        using var repo = RepoWith(
            "public class R { public object All(AppDbContext db) => "
                + "db.Notes.IgnoreQueryFilters(new[] { \"Visible\", \"Owner\" }); }"
        );

        Scan(repo, Models<NamedFilterContext>())
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Owner");
    }

    [Test]
    public void Flags_a_named_disable_whose_names_it_cannot_read()
    {
        using var repo = RepoWith(
            "public class R { public object All(AppDbContext db, string[] names) => "
                + "db.Notes.IgnoreQueryFilters(names); }"
        );

        Scan(repo, Models<NamedFilterContext>()).Offenders.Should().ContainSingle();
    }

    [Test]
    public void Ignores_a_mention_in_a_comment_or_a_string()
    {
        using var repo = RepoWith(
            "public class R {\n    // db.Notes.IgnoreQueryFilters() is not allowed here\n"
                + "    private const string Hint = \"db.Notes.IgnoreQueryFilters()\"; }"
        );

        var result = Scan(repo);

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(0);
    }

    [Test]
    public void Passes_an_allowlisted_file_with_a_reason()
    {
        using var repo = RepoWith(
            "public class NoteResolver { public IQueryable<Note> All(AppDbContext db) => "
                + "db.Notes.IgnoreQueryFilters(); }"
        );

        var result = Scan(
            repo,
            census: Census(
                new Dictionary<string, string>
                {
                    ["src/Api/NoteResolver.cs"] =
                        "the account-deletion job erases every owner's notes on purpose",
                }
            )
        );

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void Flags_an_allowlist_entry_with_no_reason()
    {
        using var repo = RepoWith(
            "public class NoteResolver { public IQueryable<Note> All(AppDbContext db) => "
                + "db.Notes.IgnoreQueryFilters(); }"
        );

        Scan(
            repo,
            census: Census(new Dictionary<string, string> { ["src/Api/NoteResolver.cs"] = " " })
        )
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("no reason");
    }

    [Test]
    public void Flags_an_allowlist_entry_that_covers_nothing()
    {
        using var repo = RepoWith("public class NoteResolver { }");

        Scan(
            repo,
            census: Census(
                new Dictionary<string, string> { ["src/Api/NoteResolver.cs"] = "used to" }
            )
        )
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("covers nothing");
    }
}
