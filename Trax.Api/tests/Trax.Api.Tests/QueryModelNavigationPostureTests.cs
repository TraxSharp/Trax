using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Configuration;
using HotChocolate.Execution;
using HotChocolate.Types.Descriptors.Configurations;
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

    private const string GatedRead =
        "{ discover { gatedPosts { nodes { title owner { apiToken } } } } }";

    private const string GatedFilter =
        "{ discover { gatedPosts(where: { owner: { apiToken: { startsWith: \"tok_live\" } } }) { nodes { title } } } }";

    private const string GatedSort =
        "{ discover { gatedPosts(order: [{ owner: { apiToken: ASC } }]) { nodes { title } } } }";

    [TestCase(GatedRead, "tok_live")]
    [TestCase(GatedFilter, "hello")]
    [TestCase(GatedSort, "hello")]
    public async Task A_target_gated_on_its_class_refuses_an_anonymous_caller_through_the_navigation(
        string query,
        string gatedValue
    )
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(executor, query, Anonymous());

        ErrorCodes(json).Should().Contain("TRAX_AUTHORIZATION", Adr);
        json.Should().NotContain(gatedValue, Adr);
    }

    [TestCase(GatedRead, "tok_live")]
    [TestCase(GatedFilter, "hello")]
    [TestCase(GatedSort, "hello")]
    public async Task A_target_gated_on_its_class_refuses_a_caller_without_its_role(
        string query,
        string gatedValue
    )
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(executor, query, InRole("Player"));

        ErrorCodes(json).Should().Contain("TRAX_AUTHORIZATION", Adr);
        json.Should().NotContain(gatedValue, Adr);
    }

    [TestCase(GatedRead, "tok_live")]
    [TestCase(GatedFilter, "hello")]
    [TestCase(GatedSort, "hello")]
    public async Task A_target_gated_on_its_class_serves_a_caller_in_its_role(
        string query,
        string gatedValue
    )
    {
        var (provider, executor) = await StartAsync<GatedTargetContext>();
        await using var _ = provider;

        var json = await RunAsync(executor, query, InRole("Admin"));

        ErrorCodes(json).Should().BeEmpty();
        json.Should().Contain(gatedValue);
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
                .SetUser(Anonymous())
                .Build()
        );

        var json = result.ExpectOperationResult().ToJson();
        ErrorCodes(json).Should().Contain("TRAX_AUTHORIZATION", Adr);
        json.Should().NotContain("hello");
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

    /// <summary>
    /// HotChocolate binds a public method of the entity as a field as readily as a property, so an
    /// entity a method returns is reached exactly as a navigation's target is.
    /// </summary>
    [Test]
    public async Task An_entity_reached_through_a_method_must_declare_its_posture()
    {
        var act = () => StartAsync<MethodUndeclaredContext>();

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should()
            .Contain(typeof(NavAccount).FullName!, Adr)
            .And.Contain("'MethodPost.primaryAccount'");
    }

    [Test]
    public async Task A_gated_entity_reached_through_a_method_refuses_an_anonymous_read()
    {
        var (provider, executor) = await StartAsync<MethodGatedContext>();
        await using var _ = provider;

        var json = await RunAsync(
            executor,
            "{ discover { methodGatedPosts { nodes { title owner { apiToken } } } } }"
        );

        json.Should().Contain("\"errors\"", Adr).And.NotContain("tok_method", Adr);
    }

    [Test]
    public async Task Every_undeclared_entity_is_named_in_one_refusal()
    {
        var act = () => StartAsync<TwoUndeclaredContext>();

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        ex.Message.Should()
            .StartWith("2 entities", Adr)
            .And.Contain(typeof(NavAccount).FullName!)
            .And.Contain(typeof(SecondAccount).FullName!);
    }

    /// <summary>
    /// Without the model's <c>DbContext</c> the check cannot tell an owned value from an entity,
    /// so it treats every class reached as an entity, and the owned value that passes above is
    /// asked to declare.
    /// </summary>
    [Test]
    public async Task A_context_that_cannot_be_resolved_fails_closed()
    {
        var (provider, _) = await StartAsync<OwnedValueContext>();
        await using var _p = provider;
        var validator = new Trax.Api.GraphQL.Startup.QueryModelReachValidator(
            provider.GetRequiredService<Trax.Api.GraphQL.Configuration.GraphQLConfiguration>(),
            new WithoutService(provider, typeof(OwnedValueContext))
        );

        var act = () => validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(typeof(Place).FullName!, Adr);
    }

    [Test]
    public async Task A_gated_target_whose_gate_a_callback_strips_is_refused()
    {
        var act = () =>
            StartAsync<GatedTargetContext>(g =>
                g.ConfigureSchema(b => b.TryAddTypeInterceptor(new StripGate(typeof(GatedOwner))))
            );

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(typeof(GatedOwner).FullName!, Adr)
            .And.Contain("carries no @authorize directive");
    }

    [Test]
    public async Task A_gated_targets_unregistered_policy_is_refused_at_startup()
    {
        var act = () => StartAsync<UnknownPolicyTargetContext>();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("NoSuchPostureTargetPolicy", Adr)
            .And.Contain(typeof(UnknownPolicyOwner).FullName!);
    }

    /// <summary>A provider, and every scope it creates, that cannot resolve one service.</summary>
    private sealed class WithoutService(IServiceProvider inner, Type hidden) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == hidden ? null
            : serviceType == typeof(IServiceScopeFactory)
                ? new ScopeFactory(inner.GetRequiredService<IServiceScopeFactory>(), hidden)
            : inner.GetService(serviceType);

        private sealed class ScopeFactory(IServiceScopeFactory inner, Type hidden)
            : IServiceScopeFactory
        {
            public IServiceScope CreateScope() => new Scope(inner.CreateScope(), hidden);
        }

        private sealed class Scope(IServiceScope inner, Type hidden) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } =
                new WithoutService(inner.ServiceProvider, hidden);

            public void Dispose() => inner.Dispose();
        }
    }

    /// <summary>A schema callback that takes the gate off one object type.</summary>
    private sealed class StripGate(Type runtimeType) : TypeInterceptor
    {
        public override void OnBeforeCompleteType(
            ITypeCompletionContext completionContext,
            TypeSystemConfiguration configuration
        )
        {
            if (configuration is not ObjectTypeConfiguration objectType)
                return;
            if (objectType.RuntimeType != runtimeType)
                return;

            foreach (
                var directive in objectType
                    .Directives.Where(d => d.Value is AuthorizeDirective)
                    .ToList()
            )
                objectType.Directives.Remove(directive);
        }
    }

    private static async Task<string> RunAsync(IRequestExecutor executor, string query) =>
        (await executor.ExecuteAsync(query)).ExpectOperationResult().ToJson();

    private static async Task<string> RunAsync(
        IRequestExecutor executor,
        string query,
        ClaimsPrincipal user
    ) =>
        (
            await executor.ExecuteAsync(
                OperationRequestBuilder.New().SetDocument(query).SetUser(user).Build()
            )
        )
            .ExpectOperationResult()
            .ToJson();

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal InRole(string role) =>
        new(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role)],
                "Test"
            )
        );

    private static List<string?> ErrorCodes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("errors", out var errors))
            return [];
        return errors
            .EnumerateArray()
            .Select(e =>
                e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                    ? c.GetString()
                    : null
            )
            .ToList();
    }

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

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class MethodPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";

        public NavAccount? GetPrimaryAccount() => null;
    }

    [TraxAuthorize(Roles = "Admin")]
    public class MethodOwner
    {
        public long Id { get; set; }
        public string ApiToken { get; set; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class MethodGatedPost
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";

        public MethodOwner GetOwner() => new() { Id = 7, ApiToken = "tok_method" };
    }

    public class SecondAccount
    {
        public long Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class TwoNavPost
    {
        public long Id { get; set; }
        public NavAccount? Account { get; set; }
        public SecondAccount? Second { get; set; }
    }

    [TraxAuthorize(Policy = "NoSuchPostureTargetPolicy")]
    public class UnknownPolicyOwner
    {
        public long Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public class UnknownPolicyPost
    {
        public long Id { get; set; }
        public UnknownPolicyOwner? Owner { get; set; }
    }

    // ── Contexts ──────────────────────────────────────────────────────────

    public class TwoUndeclaredContext(DbContextOptions<TwoUndeclaredContext> options)
        : NavContextBase(options)
    {
        public DbSet<TwoNavPost> Posts { get; set; } = null!;
        public DbSet<NavAccount> Accounts { get; set; } = null!;
        public DbSet<SecondAccount> Seconds { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class UnknownPolicyTargetContext(DbContextOptions<UnknownPolicyTargetContext> options)
        : NavContextBase(options)
    {
        public DbSet<UnknownPolicyPost> Posts { get; set; } = null!;
        public DbSet<UnknownPolicyOwner> Owners { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class MethodUndeclaredContext(DbContextOptions<MethodUndeclaredContext> options)
        : NavContextBase(options)
    {
        public DbSet<MethodPost> Posts { get; set; } = null!;
        public DbSet<NavAccount> Accounts { get; set; } = null!;

        public override Task SeedAsync() => Task.CompletedTask;
    }

    public class MethodGatedContext(DbContextOptions<MethodGatedContext> options)
        : NavContextBase(options)
    {
        public DbSet<MethodGatedPost> Posts { get; set; } = null!;
        public DbSet<MethodOwner> Owners { get; set; } = null!;

        public override async Task SeedAsync()
        {
            Posts.Add(new MethodGatedPost { Id = 1, Title = "hello" });
            await SaveChangesAsync();
        }
    }

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
