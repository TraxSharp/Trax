using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Every entity a query model reaches, through its object type or its filter and sort inputs,
/// declares its authorization posture, and the host refuses to start naming the navigation when
/// one does not. Guard for <c>docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md</c>.
/// </summary>
[Property("adr", "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md")]
[TestFixture]
public class QueryModelNavigationPostureTests
{
    private const string Adr =
        "docs/adr/0025-every-entity-a-query-model-reaches-declares-its-posture.md: every entity a "
        + "query model reaches declares its posture";

    [Test]
    public async Task The_refusal_names_the_entity_and_every_navigation_that_reaches_it()
    {
        var act = () => StartAsync<UndeclaredContext>();

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should()
            .Contain(typeof(NavAccount).FullName!, Adr)
            .And.Contain("'NavPost.account'")
            .And.Contain("'NavPostFilterInput.account'")
            .And.Contain("'NavPostSortInput.account'")
            .And.Contain("[TraxAuthorize]")
            .And.Contain("[TraxAllowAnonymous]");
    }

    [Test]
    public async Task An_endpoint_gated_with_RequireAuthorization_needs_no_declaration()
    {
        var (provider, _) = await StartAsync<GatedEndpointContext>(g => g.RequireAuthorization());
        await provider.DisposeAsync();
    }

    [Test]
    public async Task An_anonymous_target_declared_on_its_class_is_readable_and_filterable()
    {
        var (provider, executor) = await StartAsync<OpenTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { openPosts(where: { author: { handle: { eq: \"ann\" } } }) { nodes { title author { handle } } } } }"
        );

        json.Should().NotContain("\"errors\"").And.Contain("ann");
    }

    [Test]
    public async Task A_target_gated_on_its_class_refuses_an_anonymous_read_through_the_navigation()
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { gatedPosts { nodes { title owner { apiToken } } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("tok_live", Adr);
    }

    [Test]
    public async Task A_target_gated_on_its_class_refuses_an_anonymous_filter_through_the_navigation()
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { gatedPosts(where: { owner: { apiToken: { startsWith: \"tok_live\" } } }) { nodes { title } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("hello", Adr);
    }

    [Test]
    public async Task A_target_gated_on_its_class_refuses_an_anonymous_filter_passed_as_a_variable()
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(
                    "query($p: String) { discover { gatedPosts(where: { owner: { apiToken: { startsWith: $p } } }) { nodes { title } } } }"
                )
                .SetVariableValues(new Dictionary<string, object?> { ["p"] = "tok_live" })
                .Build()
        );

        result
            .ExpectOperationResult()
            .ToJson()
            .Should()
            .Contain("\"errors\"")
            .And.NotContain("hello");
    }

    [Test]
    public async Task A_target_gated_on_its_class_refuses_an_anonymous_sort_through_the_navigation()
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { gatedPosts(order: [{ owner: { apiToken: ASC } }]) { nodes { title } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("hello", Adr);
    }

    [Test]
    public async Task A_target_declaring_both_markers_is_refused()
    {
        var act = () => StartAsync<ConflictedTargetContext>();

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should().Contain(typeof(ConflictedAccount).FullName!).And.Contain("Pick one");
    }

    [Test]
    public async Task A_navigation_left_out_of_the_exposed_field_set_needs_no_declaration()
    {
        var (provider, executor) = await StartAsync<HiddenNavigationContext>();
        await using var _ = provider;

        var json = await RunAsync(executor, "{ discover { hiddenNavPosts { nodes { title } } } }");

        json.Should().NotContain("\"errors\"").And.Contain("hello");
    }

    [Test]
    public async Task An_owned_value_is_part_of_its_entity_and_needs_no_declaration()
    {
        var (provider, executor) = await StartAsync<OwnedValueContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { ownedValuePosts { nodes { title place { city } } } } }"
        );

        json.Should().NotContain("\"errors\"");
    }

    private static async Task<string> RunAsync(IRequestExecutor executor, string query) =>
        (await executor.ExecuteAsync(query)).ExpectOperationResult().ToJson();

    private static async Task<(ServiceProvider, IRequestExecutor)> StartAsync<TContext>(
        Action<TraxGraphQLBuilder>? configure = null
    )
        where TContext : NavContextBase
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        // The policy RequireAuthorization() names by default, for the gated-endpoint case.
        services.AddAuthorization(o =>
            o.AddPolicy("TraxAuthPolicy", p => p.RequireAuthenticatedUser())
        );
        var root = new InMemoryDatabaseRoot();
        var name = "NavPosture_" + Guid.NewGuid();
        services.AddDbContext<TContext>(o => o.UseInMemoryDatabase(name, root));

        services.AddTraxGraphQL(g =>
        {
            g.AddDbContext<TContext>();
            configure?.Invoke(g);
            return g;
        });

        var provider = services.BuildServiceProvider();
        try
        {
            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(default);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TContext>().SeedAsync();

        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return (provider, executor);
    }

    // ── Entities ──────────────────────────────────────────────────────────

    public class NavAccount
    {
        public long Id { get; set; }
        public string ApiToken { get; set; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class NavPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public long AccountId { get; set; }
        public NavAccount? Account { get; set; }
    }

    // No marker: the endpoint gate is its posture.
    [TraxQueryModel]
    public class GatedEndpointPost
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public NavAccount? Account { get; set; }
    }

    [TraxAllowAnonymous]
    public class OpenAuthor
    {
        public long Id { get; set; }
        public string Handle { get; set; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class OpenPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public long AuthorId { get; set; }
        public OpenAuthor? Author { get; set; }
    }

    [TraxAuthorize(Roles = "Admin")]
    public class GatedOwner
    {
        public long Id { get; set; }
        public string ApiToken { get; set; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class GatedPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public long OwnerId { get; set; }
        public GatedOwner? Owner { get; set; }
    }

    [TraxAuthorize]
    [TraxAllowAnonymous]
    public class ConflictedAccount
    {
        public long Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class ConflictedPost
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public ConflictedAccount? Account { get; set; }
    }

    [TraxQueryModel(BindFields = FieldBindingBehavior.Explicit)]
    [TraxAllowAnonymous]
    public class HiddenNavPost
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("title")]
        public string Title { get; set; } = "";

        public long AccountId { get; set; }
        public NavAccount? Account { get; set; }
    }

    public class Place
    {
        public string City { get; set; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class OwnedValuePost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public Place Place { get; set; } = new();
    }

    // ── Contexts ──────────────────────────────────────────────────────────

    public abstract class NavContextBase(DbContextOptions options) : DbContext(options)
    {
        public abstract Task SeedAsync();
    }

    public class UndeclaredContext(DbContextOptions<UndeclaredContext> options)
        : NavContextBase(options)
    {
        public DbSet<NavPost> Posts { get; set; } = null!;
        public DbSet<NavAccount> Accounts { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class GatedEndpointContext(DbContextOptions<GatedEndpointContext> options)
        : NavContextBase(options)
    {
        public DbSet<GatedEndpointPost> Posts { get; set; } = null!;
        public DbSet<NavAccount> Accounts { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class OpenTargetContext(DbContextOptions<OpenTargetContext> options)
        : NavContextBase(options)
    {
        public DbSet<OpenPost> Posts { get; set; } = null!;
        public DbSet<OpenAuthor> Authors { get; set; } = null!;

        public override async Task SeedAsync()
        {
            var author = new OpenAuthor { Id = 1, Handle = "ann" };
            Authors.Add(author);
            Posts.Add(
                new OpenPost
                {
                    Id = 1,
                    Title = "hello",
                    AuthorId = 1,
                    Author = author,
                }
            );
            await SaveChangesAsync();
        }
    }

    public class GatedTargetContext(DbContextOptions<GatedTargetContext> options)
        : NavContextBase(options)
    {
        public DbSet<GatedPost> Posts { get; set; } = null!;
        public DbSet<GatedOwner> Owners { get; set; } = null!;

        public override async Task SeedAsync()
        {
            var owner = new GatedOwner { Id = 1, ApiToken = "tok_live_42" };
            Owners.Add(owner);
            Posts.Add(
                new GatedPost
                {
                    Id = 1,
                    Title = "hello",
                    OwnerId = 1,
                    Owner = owner,
                }
            );
            await SaveChangesAsync();
        }
    }

    public class ConflictedTargetContext(DbContextOptions<ConflictedTargetContext> options)
        : NavContextBase(options)
    {
        public DbSet<ConflictedPost> Posts { get; set; } = null!;
        public DbSet<ConflictedAccount> Accounts { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class HiddenNavigationContext(DbContextOptions<HiddenNavigationContext> options)
        : NavContextBase(options)
    {
        public DbSet<HiddenNavPost> Posts { get; set; } = null!;
        public DbSet<NavAccount> Accounts { get; set; } = null!;

        public override async Task SeedAsync()
        {
            Posts.Add(new HiddenNavPost { Id = 1, Title = "hello" });
            await SaveChangesAsync();
        }
    }

    public class OwnedValueContext(DbContextOptions<OwnedValueContext> options)
        : NavContextBase(options)
    {
        public DbSet<OwnedValuePost> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<OwnedValuePost>().OwnsOne(p => p.Place);

        public override async Task SeedAsync()
        {
            Posts.Add(
                new OwnedValuePost
                {
                    Id = 1,
                    Title = "hello",
                    Place = new Place { City = "Lyon" },
                }
            );
            await SaveChangesAsync();
        }
    }
}
