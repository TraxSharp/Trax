using FluentAssertions;
using HotChocolate;
using HotChocolate.Authorization;
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
/// Inverse invariant: every <c>[TraxAllowAnonymous]</c> entity's
/// <c>ObjectType</c> and entry field MUST NOT carry a <c>@authorize</c>
/// directive after the schema is fully built. The validator is the last line
/// of defense against a consumer <c>ConfigureSchema</c> callback that
/// re-attaches <c>@authorize</c> to an entity the developer explicitly marked
/// anonymously-readable, silently re-locking what was meant to be open.
///
/// <para>
/// The corresponding positive invariant (every <c>[TraxAuthorize]</c> entity
/// keeps its directive) lives in <see cref="QueryModelAuthorizationSchemaValidator"/>;
/// this file pins the symmetric anonymous-side check.
/// </para>
/// </summary>
[TestFixture]
public class QueryModelAllowAnonymousSchemaValidatorTests
{
    [TraxQueryModel]
    [TraxAllowAnonymous]
    private class AnonThing
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class AnonDbContext(DbContextOptions<AnonDbContext> options) : DbContext(options)
    {
        public DbSet<AnonThing> Things { get; set; } = null!;
    }

    [Test]
    public async Task Validator_NoDirectiveOnEither_DoesNotThrow()
    {
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task Validator_TypeLevelDirectiveLeaked_ThrowsNamingType()
    {
        // A ConfigureSchema callback has reattached @authorize to the
        // anonymous entity's ObjectType. The validator must throw with a
        // message that names the entity and the location.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: true,
            includeAuthorizeOnField: false
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAllowAnonymous] invariant violated*")
            .WithMessage("*ObjectType*")
            .WithMessage("*AnonThing*");
    }

    [Test]
    public async Task Validator_EntryFieldDirectiveLeaked_ThrowsNamingEntryField()
    {
        // Field-level @authorize on an anonymous entity is the subtler leak:
        // the type itself is open, but the entry field rejects unauthenticated
        // callers — defeating the AllowAnonymous opt-in.
        var (config, services) = await BuildSchemaAsync(
            includeAuthorizeOnType: false,
            includeAuthorizeOnField: true
        );
        var validator = new QueryModelAuthorizationSchemaValidator(config, services);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[TraxAllowAnonymous] invariant violated*")
            .WithMessage("*entry field*")
            .WithMessage("*anonThings*");
    }

    [Test]
    public async Task Validator_NoAllowAnonymousEntities_DoesNotMaterialiseSchema()
    {
        // Mirror of the positive validator's short-circuit: a host with only
        // gated or bare entities must not pay the schema-materialisation cost
        // for an empty AllowAnonymous walk. Pin it so a refactor doesn't
        // accidentally turn the validator into an always-resolve cost.
        var services = new ServiceCollection();
        var config = new TraxGraphQLBuilder(services).Build();
        config.ModelRegistrations.Should().BeEmpty();

        var throwingProvider = new ThrowingServiceProvider();
        var validator = new QueryModelAuthorizationSchemaValidator(config, throwingProvider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync(
                "no AllowAnonymous entities means the validator must not resolve the schema"
            );
        throwingProvider.GetServiceCallCount.Should().Be(0);
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public int GetServiceCallCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            GetServiceCallCount++;
            throw new InvalidOperationException(
                "validator must not resolve any services when no entities require checking."
            );
        }
    }

    private static async Task<(
        GraphQLConfiguration Config,
        IServiceProvider Services
    )> BuildSchemaAsync(bool includeAuthorizeOnType, bool includeAuthorizeOnField)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();

        var config = new TraxGraphQLBuilder(services).AddDbContext<AnonDbContext>().Build();
        services.AddSingleton(config);
        services.AddDbContextFactory<AnonDbContext>(o =>
            o.UseInMemoryDatabase("anonsv-" + Guid.NewGuid())
        );

        var gql = services.AddGraphQLServer("trax").AddAuthorization();

        ObjectType<AnonThing> objectType = includeAuthorizeOnType
            ? new ObjectType<AnonThing>(d => d.Authorize(new[] { "Admin" }))
            : new ObjectType<AnonThing>();
        gql.AddType(objectType);

        gql.AddQueryType(d =>
        {
            d.Name("RootQuery");
            d.Field("discover").Type<DiscoverObjectType>().Resolve(_ => new object());
        });

        gql.AddTypeExtension(
            new ObjectTypeExtension(d =>
            {
                d.Name("DiscoverQueries");
                var field = d.Field("anonThings")
                    .Type<ListType<ObjectType<AnonThing>>>()
                    .Resolve(_ => Array.Empty<AnonThing>());
                if (includeAuthorizeOnField)
                    field.Authorize(new[] { "Admin" });
            })
        );

        var sp = services.BuildServiceProvider();
        var resolver = sp.GetRequiredService<IRequestExecutorProvider>();
        _ = await resolver.GetExecutorAsync("trax");

        return (config, sp);
    }

    private sealed class DiscoverObjectType : ObjectType
    {
        protected override void Configure(IObjectTypeDescriptor descriptor) =>
            descriptor.Name("DiscoverQueries");
    }
}
