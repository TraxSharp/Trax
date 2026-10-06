using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Every field that takes a <c>where</c> or <c>order</c> argument carries the authorization of the
/// types those inputs reach, whoever contributed the field: a query model's entry field, a type
/// extension's resolver, or a filtered collection navigation on an entity. Runs on Postgres, so
/// the rows a filter selects are the ones the database selected. Guard for
/// <c>docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md")]
[TestFixture]
public class FilterInputPostureTests
{
    private const string Database = "trax_api_posture_inputs";

    private const string Adr =
        "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md: a where or "
        + "order that reaches a gated type is authorized as if the type were selected";

    private const string ExtensionWhere =
        "{ discover { searchInputPosts(where: { owner: { email: { eq: \"ada@example.test\" } } }) { title } } }";

    private const string ExtensionOrder =
        "{ discover { searchInputPosts(order: [{ owner: { email: ASC } }]) { title } } }";

    private const string NavigationWhere =
        "{ discover { inputPosts { nodes { title comments(where: { author: { email: { eq: \"ada@example.test\" } } }) { body } } } } }";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);

        var options = new DbContextOptionsBuilder<InputPostureContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var context = new InputPostureContext(options))
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            var ada = new InputOwner { Id = 1, Email = "ada@example.test" };
            var bob = new InputOwner { Id = 2, Email = "bob@example.test" };
            context.Owners.AddRange(ada, bob);
            context.Posts.AddRange(
                new InputPost
                {
                    Id = 1,
                    Title = "first",
                    Owner = ada,
                    Comments =
                    [
                        new InputComment
                        {
                            Id = 1,
                            Body = "by ada",
                            Author = ada,
                        },
                    ],
                },
                new InputPost
                {
                    Id = 2,
                    Title = "second",
                    Owner = bob,
                    Comments =
                    [
                        new InputComment
                        {
                            Id = 2,
                            Body = "by bob",
                            Author = bob,
                        },
                    ],
                }
            );
            await context.SaveChangesAsync();
        }

        _host = await PostureHost.StartAsync(
            g =>
                g.AddDbContext<InputPostureContext>()
                    .ConfigureSchema(b => b.AddTypeExtension<InputSearchExtension>()),
            s => s.AddDbContextFactory<InputPostureContext>(o => o.UseNpgsql(connectionString))
        );
    }

    [OneTimeTearDown]
    public void StopAsync() => _host.Dispose();

    // ── A type extension's filtered field ────────────────────────────────

    [Test]
    public async Task AnExtensionFieldsWhereThroughAGatedNavigation_RefusesAnAnonymousCaller()
    {
        (await _host.PostAsync(ExtensionWhere))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr)
            .And.NotContain("first");
    }

    [Test]
    public async Task AnExtensionFieldsWhereThroughAGatedNavigation_RefusesACallerWithoutTheRole()
    {
        (await _host.PostAsync(ExtensionWhere, PostureHost.SupportKey))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr)
            .And.NotContain("first");
    }

    [Test]
    public async Task AnExtensionFieldsWhereThroughAGatedNavigation_ServesACallerInTheRole()
    {
        (await _host.PostAsync(ExtensionWhere, PostureHost.AdminKey))
            .Should()
            .Contain("\"first\"")
            .And.NotContain("\"second\"")
            .And.NotContain("\"errors\"");
    }

    [Test]
    public async Task AnExtensionFieldsOrderThroughAGatedNavigation_RefusesAnAnonymousCaller()
    {
        (await _host.PostAsync(ExtensionOrder))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr)
            .And.NotContain("first");
    }

    [Test]
    public async Task AnExtensionFieldsOrderThroughAGatedNavigation_ServesACallerInTheRole()
    {
        (await _host.PostAsync(ExtensionOrder, PostureHost.AdminKey))
            .Should()
            .Contain("\"first\"")
            .And.NotContain("\"errors\"");
    }

    [Test]
    public async Task AnExtensionFieldsWhereOverOpenColumns_ServesAnAnonymousCaller()
    {
        (
            await _host.PostAsync(
                "{ discover { searchInputPosts(where: { title: { eq: \"second\" } }) { title } } }"
            )
        )
            .Should()
            .Contain("\"second\"")
            .And.NotContain("\"errors\"");
    }

    // ── A filtered collection navigation on an entity ────────────────────

    [Test]
    public async Task ANavigationsWhereThroughAGatedNavigation_RefusesAnAnonymousCaller()
    {
        (await _host.PostAsync(NavigationWhere))
            .Should()
            .Contain("TRAX_AUTHORIZATION", Adr)
            .And.NotContain("by ada");
    }

    /// <summary>
    /// The role holder is let through. What the nested filter then selects is HotChocolate's
    /// business: under projection it compares a navigation the projection did not load, so it
    /// matches nothing, which is why this asserts only that the caller is not refused.
    /// </summary>
    [Test]
    public async Task ANavigationsWhereThroughAGatedNavigation_AdmitsACallerInTheRole()
    {
        (await _host.PostAsync(NavigationWhere, PostureHost.AdminKey))
            .Should()
            .Contain("\"first\"")
            .And.NotContain("\"errors\"");
    }

    [Test]
    public async Task ANavigationsWhereOverOpenColumns_ServesAnAnonymousCaller()
    {
        (
            await _host.PostAsync(
                "{ discover { inputPosts { nodes { comments(where: { body: { eq: \"by bob\" } }) { body } } } } }"
            )
        )
            .Should()
            .Contain("\"by bob\"")
            .And.NotContain("\"by ada\"")
            .And.NotContain("\"errors\"");
    }

    // ── Model ────────────────────────────────────────────────────────────

    public sealed class InputPostureContext(DbContextOptions<InputPostureContext> options)
        : DbContext(options)
    {
        public DbSet<InputPost> Posts => Set<InputPost>();

        public DbSet<InputOwner> Owners => Set<InputOwner>();

        public DbSet<InputComment> Comments => Set<InputComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasDefaultSchema("posture_inputs");
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public sealed class InputPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public int OwnerId { get; set; }

        public InputOwner? Owner { get; set; }

        [UseFiltering]
        public List<InputComment> Comments { get; set; } = [];
    }

    [TraxAuthorize(Roles = "Admin")]
    public sealed class InputOwner
    {
        public int Id { get; set; }

        public string Email { get; set; } = "";
    }

    [TraxAllowAnonymous]
    public sealed class InputComment
    {
        public int Id { get; set; }

        public string Body { get; set; } = "";

        public int InputPostId { get; set; }

        public int AuthorId { get; set; }

        public InputOwner? Author { get; set; }
    }

    /// <summary>
    /// Built with a descriptor rather than <c>[ExtendObjectType]</c> and <c>[UseFiltering]</c>
    /// attributes, so a test that scans this assembly for type extensions does not pick it up.
    /// </summary>
    public sealed class InputSearchExtension : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("DiscoverQueries");
            descriptor
                .Field<InputSearch>(s => s.SearchInputPosts(default!))
                .UseFiltering()
                .UseSorting();
        }
    }

    public sealed class InputSearch
    {
        [TraxAllowAnonymous]
        public IQueryable<InputPost> SearchInputPosts([Service] InputPostureContext context) =>
            context.Posts;
    }
}
