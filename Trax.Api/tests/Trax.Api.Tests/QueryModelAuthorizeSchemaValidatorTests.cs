using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Startup;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="QueryModelAuthorizationSchemaValidator"/>.
/// Builds GraphQL schemas by hand where the <c>@authorize</c> directive has
/// been deliberately stripped from a gated entity, then verifies the validator
/// throws at <c>StartAsync</c> with a message naming the entity and the
/// missing gate location. This is the suite that proves the validator's
/// failure paths — the corresponding success path runs through the full
/// <c>AddTraxGraphQL</c> pipeline in
/// <c>QueryModelAuthorizeSchemaInvariantE2ETests</c>.
/// </summary>
[TestFixture]
public class QueryModelAuthorizeSchemaValidatorTests
{
    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    private class GatedThing
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class GatedDbContext(DbContextOptions<GatedDbContext> options) : DbContext(options)
    {
        public DbSet<GatedThing> Things { get; set; } = null!;
    }

    [Test]
    public async Task Validator_BothDirectivesStripped_ThrowsNamingEntity()
    {
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*GatedThing*");
    }

    [Test]
    public async Task Validator_OnlyFieldDirectiveStripped_ThrowsNamingEntryField()
    {
        // Type-level gate is present, but the entry field has been stripped.
        // This is the more subtle failure — transitive navigation would still
        // be blocked, but `totalCount` and `pageInfo` would leak. The
        // validator must still catch it.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*entry field*")
            .WithMessage("*gatedThings*");
    }

    [Test]
    public async Task Validator_OnlyTypeDirectiveStripped_ThrowsNamingType()
    {
        // Entry field guarded but the type itself isn't — transitive nav
        // through this type from another (ungated) entity would leak rows.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAuthorize] invariant violated*")
            .WithMessage("*ObjectType*")
            .WithMessage("*GatedThing*");
    }

    [Test]
    public async Task Validator_BothDirectivesPresent_DoesNotThrow()
    {
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    // ── Schema-shape failure paths ───────────────────────────────────────
    //
    // Beyond directive-stripping, the validator must also catch a hostile
    // ConfigureSchema callback that *deletes* parts of the schema the gate
    // relies on. Each test below removes one expected element and confirms
    // the validator throws with a message that names what is missing.

    /// <summary>
    /// A consumer's <c>ConfigureSchema</c> callback removes the
    /// <c>discover</c> root field, so the validator cannot navigate to the
    /// gated entity's entry field at all. The validator must throw rather
    /// than silently passing the now-unreachable entity.
    /// </summary>
    [Test]
    public async Task Validator_DiscoverFieldMissing_ThrowsNamingDiscover()
    {
        var (config, services) = await BuildSchemaAsync(includeDiscoverField: false);
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*`discover` field is not present*")
            .WithMessage("*GatedThing*");
    }

    /// <summary>
    /// A consumer keeps <c>discover</c> but the entry field
    /// (<c>gatedThings</c>) has been removed. The validator must throw
    /// rather than passing on the assumption that "absence implies
    /// inaccessibility" — the test pins that the validator is strict about
    /// every gated entity having a reachable entry.
    /// </summary>
    [Test]
    public async Task Validator_EntryFieldMissing_ThrowsNamingEntryField()
    {
        var (config, services) = await BuildSchemaAsync(includeEntryField: false);
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*entry field*missing*")
            .WithMessage("*gatedThings*");
    }

    /// <summary>
    /// Happy path for entities declared with a <c>Namespace</c>: the
    /// validator must descend into the namespace intermediate type and find
    /// the entry field there. Without this test, a refactor that broke the
    /// namespace walk would only surface on namespaced models in production.
    /// </summary>
    [Test]
    public async Task Validator_NamespacedEntity_BothDirectivesPresent_DoesNotThrow()
    {
        var (config, services) = await BuildNamespacedSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: true,
            includeNamespaceField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    /// <summary>
    /// A namespaced entity where the namespace intermediate type
    /// (<c>discover.testns</c>) is missing. The validator must report the
    /// namespace field rather than the entry field — the failure-mode
    /// distinction matters when a maintainer is reading the error message
    /// trying to figure out which override broke things.
    /// </summary>
    [Test]
    public async Task Validator_NamespaceFieldMissing_ThrowsNamingNamespaceField()
    {
        var (config, services) = await BuildNamespacedSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: true,
            includeNamespaceField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*namespace field*")
            .WithMessage("*testns*");
    }

    /// <summary>
    /// HotChocolate accepts a schema in which the gated entity's
    /// <c>ObjectType</c> is never registered, as long as nothing references it.
    /// Production code always registers it via the type module, but a hostile
    /// <c>ConfigureSchema</c> that fully replaces the discover surface could
    /// leave us without it. The validator must throw with a message that names
    /// the entity rather than silently passing on an unreachable gate.
    /// </summary>
    [Test]
    public async Task Validator_ObjectTypeMissingFromSchema_ThrowsNamingMissingType()
    {
        var (config, services) = await BuildSchemaWithoutGatedObjectTypeAsync();
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no ObjectType*")
            .WithMessage("*GatedThing*");
    }

    /// <summary>
    /// IHostedService contract: <c>StopAsync</c> must return a synchronously
    /// completed task — the validator holds no resources to release. Pinning
    /// this catches a future refactor that accidentally introduces async
    /// cleanup without explicit consideration of the host lifecycle.
    /// </summary>
    [Test]
    public async Task Validator_StopAsync_CompletesSynchronously()
    {
        var emptyConfig = new TraxGraphQLBuilder(new ServiceCollection()).Build();
        var validator = new QueryModelAuthorizationSchemaValidator(
            emptyConfig,
            new ServiceCollection().BuildServiceProvider()
        );

        var task = validator.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    /// <summary>
    /// The validator must short-circuit when no <c>[TraxQueryModel]</c>
    /// entity is gated. Important because it means a host with only ungated
    /// query models does not pay the schema-materialisation cost at startup.
    /// </summary>
    [Test]
    public async Task Validator_NoGatedEntities_ReturnsWithoutResolvingSchema()
    {
        var services = new ServiceCollection();
        // Build a configuration whose entity carries [TraxQueryModel] but
        // no [TraxAuthorize]. The validator should never touch the service
        // provider, so an empty provider is sufficient to prove it.
        // The endpoint is gated (RequireAuthorization) so a marker-less entity is
        // a valid exposure: this keeps the entity ungated (no [TraxAuthorize], no
        // [TraxAllowAnonymous]) so the validator has zero gated AND zero anonymous
        // entities and must short-circuit without resolving the schema.
        var config = new TraxGraphQLBuilder(services)
            .RequireAuthorization()
            .AddDbContext<UngatedDbContext>()
            .Build();
        config.ModelRegistrations.Should().NotBeEmpty("the test fixture must register a model");
        config
            .ModelRegistrations.Single()
            .AuthorizeAttributes.Should()
            .BeEmpty("the test fixture's entity must be ungated");

        var throwingProvider = new ThrowingServiceProvider();
        var validator = new QueryModelAuthorizationSchemaValidator(config, throwingProvider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync("no gated entities means the validator must not resolve the schema");
        throwingProvider.CreateScopeCallCount.Should().Be(0);
    }

    [TraxQueryModel]
    private class UngatedThing
    {
        public int Id { get; set; }
    }

    private class UngatedDbContext(DbContextOptions<UngatedDbContext> options) : DbContext(options)
    {
        public DbSet<UngatedThing> Things { get; set; } = null!;
    }

    /// <summary>
    /// Service provider that throws on any access, used to prove the
    /// validator's early-return path never consults DI when there is no
    /// work to do.
    /// </summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public int CreateScopeCallCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                CreateScopeCallCount++;
            }
            throw new InvalidOperationException(
                "validator must not resolve any services when no entities are gated."
            );
        }
    }

    /// <summary>
    /// Builds the canonical "single ungated namespace" schema variant the
    /// extra tests share. Mirrors <see cref="BuildSchemaAsync"/>, parameterised
    /// to omit either the discover root field or the entry field on the
    /// namespace.
    /// </summary>
    private static async Task<(
        GraphQLConfiguration Config,
        IServiceProvider Services
    )> BuildSchemaAsync(
        bool includeDiscoverField = true,
        bool includeEntryField = true,
        bool includeAuthorizeOnType = true,
        bool includeAuthorizeOnField = true
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();

        var config = new TraxGraphQLBuilder(services).AddDbContext<GatedDbContext>().Build();
        services.AddSingleton(config);
        services.AddDbContextFactory<GatedDbContext>(o => o.UseInMemoryDatabase("svtests-shape"));

        var gql = services.AddGraphQLServer("trax").AddAuthorization();

        ObjectType<GatedThing> objectType = includeAuthorizeOnType
            ? new ObjectType<GatedThing>(d => d.Authorize(new[] { "Admin" }))
            : new ObjectType<GatedThing>();
        gql.AddType(objectType);

        gql.AddQueryType(d =>
        {
            d.Name("RootQuery");
            if (includeDiscoverField)
                d.Field("discover").Type<DiscoverObjectType>().Resolve(_ => new object());
            else
                // RootQuery must have at least one field or HC refuses to
                // build. Add a sentinel that the validator does not look at.
                d.Field("ping").Type<HotChocolate.Types.StringType>().Resolve(_ => "pong");
        });

        if (includeDiscoverField)
        {
            gql.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    if (!includeEntryField)
                    {
                        // DiscoverQueries needs at least one field to be a
                        // valid GraphQL type even when the entry field for
                        // GatedThing is intentionally absent.
                        d.Field("sentinel")
                            .Type<HotChocolate.Types.StringType>()
                            .Resolve(_ => "ok");
                        return;
                    }

                    var field = d.Field("gatedThings")
                        .Type<ListType<ObjectType<GatedThing>>>()
                        .Resolve(_ => Array.Empty<GatedThing>());
                    if (includeAuthorizeOnField)
                        field.Authorize(new[] { "Admin" });
                })
            );
        }

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorProvider>();
        _ = await resolver.GetExecutorAsync("trax");

        return (config, sp);
    }

    /// <summary>
    /// Variant of <see cref="BuildSchemaAsync"/> for entities declared with
    /// a <see cref="TraxQueryModelAttribute.Namespace"/>. The entity here is
    /// <see cref="NamespacedGatedThing"/> with <c>Namespace = "testns"</c>;
    /// the validator must descend through <c>discover.testns.namespacedGatedThings</c>.
    /// </summary>
    private static async Task<(
        GraphQLConfiguration Config,
        IServiceProvider Services
    )> BuildNamespacedSchemaAsync(
        bool includeAuthorizeOnType,
        bool includeAuthorizeOnField,
        bool includeNamespaceField
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();

        var config = new TraxGraphQLBuilder(services)
            .AddDbContext<NamespacedGatedDbContext>()
            .Build();
        services.AddSingleton(config);
        services.AddDbContextFactory<NamespacedGatedDbContext>(o =>
            o.UseInMemoryDatabase("svtests-ns")
        );

        var gql = services.AddGraphQLServer("trax").AddAuthorization();

        ObjectType<NamespacedGatedThing> objectType = includeAuthorizeOnType
            ? new ObjectType<NamespacedGatedThing>(d => d.Authorize(new[] { "Admin" }))
            : new ObjectType<NamespacedGatedThing>();
        gql.AddType(objectType);

        gql.AddQueryType(d =>
        {
            d.Name("RootQuery");
            d.Field("discover").Type<DiscoverObjectType>().Resolve(_ => new object());
        });

        if (includeNamespaceField)
        {
            gql.AddType(new ObjectType(d => d.Name("DiscoverQueries_testns")));
            gql.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    d.Field("testns")
                        .Type(new HotChocolate.Language.NamedTypeNode("DiscoverQueries_testns"))
                        .Resolve(_ => new object());
                })
            );
            gql.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries_testns");
                    var field = d.Field("namespacedGatedThings")
                        .Type<ListType<ObjectType<NamespacedGatedThing>>>()
                        .Resolve(_ => Array.Empty<NamespacedGatedThing>());
                    if (includeAuthorizeOnField)
                        field.Authorize(new[] { "Admin" });
                })
            );
        }
        else
        {
            gql.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    d.Field("sentinel").Type<HotChocolate.Types.StringType>().Resolve(_ => "ok");
                })
            );
        }

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorProvider>();
        _ = await resolver.GetExecutorAsync("trax");

        return (config, sp);
    }

    /// <summary>
    /// Builds a schema where the gated entity's <see cref="ObjectType"/> is
    /// never registered. The DiscoverQueries placeholder field returns a
    /// string so HC can build the schema without referencing GatedThing's
    /// CLR type at all.
    /// </summary>
    private static async Task<(
        GraphQLConfiguration Config,
        IServiceProvider Services
    )> BuildSchemaWithoutGatedObjectTypeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();

        var config = new TraxGraphQLBuilder(services).AddDbContext<GatedDbContext>().Build();
        services.AddSingleton(config);
        services.AddDbContextFactory<GatedDbContext>(o => o.UseInMemoryDatabase("svtests-noobj"));

        var gql = services.AddGraphQLServer("trax").AddAuthorization();

        // NO AddType<ObjectType<GatedThing>>. RootQuery exposes a `discover`
        // field but DiscoverQueries has only a placeholder string field —
        // GatedThing's CLR type is never reachable from the schema, so HC
        // will not auto-discover the ObjectType either.
        gql.AddQueryType(d =>
        {
            d.Name("RootQuery");
            d.Field("discover").Type<DiscoverObjectType>().Resolve(_ => new object());
        });

        gql.AddTypeExtension(
            new ObjectTypeExtension(d =>
            {
                d.Name("DiscoverQueries");
                d.Field("placeholder").Type<HotChocolate.Types.StringType>().Resolve(_ => "ok");
            })
        );

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorProvider>();
        _ = await resolver.GetExecutorAsync("trax");

        return (config, sp);
    }

    [TraxQueryModel(Namespace = "testns")]
    [TraxAuthorize(Roles = "Admin")]
    private class NamespacedGatedThing
    {
        public int Id { get; set; }
    }

    private class NamespacedGatedDbContext(DbContextOptions<NamespacedGatedDbContext> options)
        : DbContext(options)
    {
        public DbSet<NamespacedGatedThing> Things { get; set; } = null!;
    }

    private sealed class DiscoverObjectType : ObjectType
    {
        protected override void Configure(IObjectTypeDescriptor descriptor) =>
            descriptor.Name("DiscoverQueries");
    }
}
