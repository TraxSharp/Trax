using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Startup;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The exposure census for fields added by a type extension, driven through a real host so the
/// answer comes from the schema HotChocolate actually built. The decision matrix itself is pinned
/// in <see cref="TypeExtensionExposureRuleTests"/>; these verify the wiring: which fields the
/// census can see, what it resolves their parent to be, and that the host refuses to start.
/// </summary>
[Property("adr", "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md")]
[TestFixture]
public class TypeExtensionExposureTests
{
    /// <summary>
    /// Starts a host with the given schema configuration and returns the startup exception, or
    /// null when it started. Hosted services run on start, which is what makes this a startup
    /// failure rather than a first-request one.
    /// </summary>
    private static async Task<Exception?> StartAsync(
        Action<TraxGraphQLBuilder> configure,
        bool exposeOperations = false,
        bool gateEndpoint = false
    )
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();

                // RequireAuthorization() gates on the combined Trax policy, which only exists once
                // a scheme has contributed to it.
                if (gateEndpoint)
                    services.AddTraxApiKeyAuth(keys => keys.Add("census-key", id: "census"));

                services.AddSingleton<TraxMarker>();
                var discovery = Substitute.For<ITrainDiscoveryService>();
                discovery.DiscoverTrains().Returns([]);
                services.AddSingleton(discovery);
                services.AddSingleton(Substitute.For<IEffectRegistry>());

                services.AddDbContextFactory<CensusDbContext>(o =>
                    o.UseInMemoryDatabase("census-" + Guid.NewGuid())
                );
                services.AddDbContextFactory<GatedOnlyDbContext>(o =>
                    o.UseInMemoryDatabase("census-gated-" + Guid.NewGuid())
                );

                services.AddTraxGraphQL(graphql =>
                {
                    // A gated endpoint cannot carry a [TraxAllowAnonymous] entity: that pairing is
                    // itself a violation of the entity rule, so those hosts get a context holding
                    // only the gated entity.
                    if (gateEndpoint)
                        graphql.AddDbContext<GatedOnlyDbContext>().RequireAuthorization();
                    else
                        graphql.AddDbContext<CensusDbContext>();

                    if (exposeOperations)
                        graphql.ExposeOperationMutations().AllowAnonymousOperations();
                    configure(graphql);
                    return graphql;
                });

                // Backing services for the operations resolvers, so exposing the namespace does
                // not fail for an unrelated reason.
                services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                services.AddScoped(_ => Substitute.For<IOperationsService>());
                services.AddScoped(_ => Substitute.For<ITraxScheduler>());
            })
            .Build();

        try
        {
            await host.StartAsync();
            await host.StopAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            host.Dispose();
        }
    }

    // ── A field on a [TraxAllowAnonymous] parent inherits nothing ────────

    [Test]
    public async Task AnonymousParent_UndeclaredField_FailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<UndeclaredOnPublicThing>());

        ex.Should()
            .BeOfType<InvalidOperationException>(
                "a field on an anonymous parent inherits no gate, per "
                    + "docs/adr/0003-a-type-extension-field-declares-its-own-posture.md"
            );
        ex!.Message.Should().Contain("PublicThing.undeclared");
        ex.Message.Should().Contain(nameof(UndeclaredOnPublicThing));
        ex.Message.Should().Contain("[TraxAllowAnonymous]");
    }

    [Test]
    public async Task AnonymousParent_FieldWithAuthorize_Starts()
    {
        (await StartAsync(g => g.AddTypeExtension<GatedOnPublicThing>())).Should().BeNull();
    }

    [Test]
    public async Task AnonymousParent_FieldWithAllowAnonymous_Starts()
    {
        (await StartAsync(g => g.AddTypeExtension<AnonymousOnPublicThing>())).Should().BeNull();
    }

    [Test]
    public async Task AnonymousParent_FieldWithBothAttributes_FailsStartupAsAConflict()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<ConflictedOnPublicThing>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("PublicThing.conflicted");
        ex.Message.Should().Contain("Pick one");
    }

    /// <summary>
    /// The reason Trax owns this attribute rather than deferring to HotChocolate's. A class-level
    /// <c>[TraxAuthorize]</c> on an extension gates the fields that extension contributes and
    /// leaves the type being extended alone, so a <c>[TraxAllowAnonymous]</c> entity stays
    /// anonymous. HotChocolate's attribute applies to the extended type instead, which re-locks
    /// the whole entity and trips the query-model invariant.
    /// </summary>
    [Test]
    public async Task AnonymousParent_ClassLevelTraxAuthorize_GatesTheFieldAndLeavesTheEntityOpen()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<ClassGatedOnPublicThing>());

        ex.Should()
            .BeNull(
                "the entity keeps its [TraxAllowAnonymous] posture, so the query-model schema "
                    + "invariant is not tripped"
            );
    }

    /// <summary>
    /// Same on a root type, where HotChocolate's attribute would have set the posture of every
    /// operation in the schema.
    /// </summary>
    [Test]
    public async Task RootType_ClassLevelTraxAuthorize_GatesOnlyThatExtensionsFields()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<ClassGatedOnRootMutation>(),
            exposeOperations: true
        );

        ex.Should().BeNull();
    }

    // ── A field on a gated parent inherits the gate ──────────────────────

    [Test]
    public async Task GatedParent_UndeclaredField_Starts()
    {
        (await StartAsync(g => g.AddTypeExtension<UndeclaredOnGatedThing>())).Should().BeNull();
    }

    // ── A field on a type that is not an exposed surface ─────────────────

    /// <summary>
    /// <c>OperationsMutations</c> is not a query model and not a root type: it reaches the schema
    /// only under the <c>operations</c> field, whose posture governs. This is the row that keeps
    /// the census from failing every host that uses persisted operations, whose management
    /// mutations are exactly this shape.
    /// </summary>
    [Test]
    public async Task UnexposedParent_UndeclaredField_Starts()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<UndeclaredOnOperationsMutations>(),
            exposeOperations: true
        );

        ex.Should().BeNull();
    }

    // ── Root types have no parent to inherit from ────────────────────────

    [Test]
    public async Task RootQuery_UndeclaredField_FailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<UndeclaredOnRootQuery>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("RootQuery.undeclaredRootQueryField");
        ex.Message.Should().Contain("schema root type");
    }

    [Test]
    public async Task RootMutation_UndeclaredField_FailsStartup()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<UndeclaredOnRootMutation>(),
            exposeOperations: true
        );

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("RootMutation.undeclaredRootMutationField");
    }

    /// <summary>
    /// The subscription case, which is the same construct and the reason a consumer had to
    /// hand-write a repo guard requiring <c>[Authorize]</c> on every <c>[Subscribe]</c> field.
    /// Note the extension names its target as a string, so there is no CLR type on the attribute
    /// to resolve: the census reads the merged type instead, which is what makes this work.
    /// </summary>
    [Test]
    public async Task Subscription_UndeclaredField_FailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<UndeclaredSubscription>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("LifecycleSubscriptions.undeclaredStream");
        ex.Message.Should().Contain("schema root type");
    }

    [Test]
    public async Task Subscription_WithAllowAnonymous_Starts()
    {
        (await StartAsync(g => g.AddTypeExtension<AnonymousSubscription>())).Should().BeNull();
    }

    // ── Another framework's vocabulary is refused ───────────────────────

    /// <summary>
    /// HotChocolate's attribute compiles on a resolver and HotChocolate would honour it, which is
    /// exactly why Trax refuses it: the field would be gated by a vocabulary Trax does not read,
    /// so the census could not tell a deliberate posture from an accident.
    /// </summary>
    [Test]
    public async Task ForeignAuthorizeAttribute_FailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<ForeignGatedOnPublicThing>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("PublicThing.foreignGated");
        ex.Message.Should().Contain("[Authorize]");
        ex.Message.Should().Contain("[TraxAuthorize]");
    }

    [Test]
    public async Task ForeignAllowAnonymousAttribute_FailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<ForeignAnonymousOnPublicThing>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("[TraxAllowAnonymous]");
    }

    /// <summary>
    /// Refused even on a gated parent, where the field needs no marker at all. The objection is
    /// not that the field is ungated; it is that one surface is being described in two
    /// vocabularies.
    /// </summary>
    [Test]
    public async Task ForeignAttributeOnAGatedParent_StillFailsStartup()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<ForeignGatedOnGatedThing>());

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("which Trax does not read");
    }

    // ── Endpoint posture ─────────────────────────────────────────────────

    /// <summary>
    /// The endpoint gate covers every field behind it, so nothing has to declare. A root-type
    /// extension is the subject here because <c>[TraxAllowAnonymous]</c> on an entity is itself
    /// rejected under <c>RequireAuthorization()</c>, so no anonymous entity can exist to hang a
    /// field on.
    /// </summary>
    [Test]
    public async Task EndpointGated_UndeclaredField_Starts()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<UndeclaredOnRootQuery>(),
            gateEndpoint: true
        );

        ex.Should().BeNull();
    }

    [Test]
    public async Task EndpointGated_ConflictedField_StillFailsStartup()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<ConflictedOnRootQuery>(),
            gateEndpoint: true
        );

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("Pick one");
    }

    // ── Completeness ─────────────────────────────────────────────────────

    /// <summary>
    /// The claim that makes the merged-type walk worth its cost. <c>ConfigureSchema</c> has the
    /// whole <c>IRequestExecutorBuilder</c>, so this extension never appears in
    /// <c>AdditionalTypeExtensions</c>, and a census built from that list would not see it.
    /// </summary>
    [Test]
    public async Task TypeExtensionAddedThroughConfigureSchema_IsStillCaught()
    {
        var ex = await StartAsync(g =>
            g.ConfigureSchema(b => b.AddTypeExtension<UndeclaredViaConfigureSchema>())
        );

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("PublicThing.viaConfigureSchema");
    }

    [Test]
    public async Task EveryViolation_IsReportedInOneMessage()
    {
        var ex = await StartAsync(g =>
            g.AddTypeExtension<UndeclaredOnPublicThing>()
                .AddTypeExtension<SecondUndeclaredOnPublicThing>()
                .AddTypeExtension<UndeclaredOnRootQuery>()
        );

        ex.Should().BeOfType<InvalidOperationException>();
        ex!.Message.Should().Contain("3 GraphQL field(s)");
        ex.Message.Should().Contain("PublicThing.undeclared");
        ex.Message.Should().Contain("PublicThing.secondUndeclared");
        ex.Message.Should().Contain("RootQuery.undeclaredRootQueryField");
    }

    /// <summary>
    /// An entity's own properties are not type-extension fields, however many of them there are,
    /// and neither are Trax's own <c>discover</c> and <c>operations</c> entry fields, which are
    /// built from a lambda and have no member to carry an attribute.
    /// </summary>
    [Test]
    public async Task OrdinaryEntityFieldsAndTraxsOwnEntryFields_AreNotFlagged()
    {
        var ex = await StartAsync(
            g => g.AddTypeExtension<AnonymousOnPublicThing>(),
            exposeOperations: true
        );

        ex.Should().BeNull();
    }

    /// <summary>
    /// A property inherited from a base class is declared by the base, not by the entity, so a
    /// naive "declaring type is not the runtime type" test would read it as bolted on.
    /// </summary>
    [Test]
    public async Task InheritedEntityProperty_IsNotFlagged()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<AnonymousOnPublicThing>());

        ex.Should().BeNull();
    }

    // ── Registration ─────────────────────────────────────────────────────

    [Test]
    public void NoTypeExtensionsAndNoSchemaCallback_DoesNotRegisterTheCensus()
    {
        var services = BaseServices();
        services.AddTraxGraphQL(g => g.AddDbContext<CensusDbContext>());

        services
            .Any(sd => sd.ImplementationType == typeof(TypeExtensionExposureValidator))
            .Should()
            .BeFalse("a host with no type extension cannot have a type-extension field");
    }

    [Test]
    public void ATypeExtension_RegistersTheCensus()
    {
        var services = BaseServices();
        services.AddTraxGraphQL(g =>
            g.AddDbContext<CensusDbContext>().AddTypeExtension<AnonymousOnPublicThing>()
        );

        services
            .Any(sd => sd.ImplementationType == typeof(TypeExtensionExposureValidator))
            .Should()
            .BeTrue();
    }

    [Test]
    public void AConfigureSchemaCallback_RegistersTheCensus()
    {
        var services = BaseServices();
        services.AddTraxGraphQL(g => g.AddDbContext<CensusDbContext>().ConfigureSchema(_ => { }));

        services
            .Any(sd => sd.ImplementationType == typeof(TypeExtensionExposureValidator))
            .Should()
            .BeTrue("a callback with full builder access can add one Trax never sees");
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddDbContextFactory<CensusDbContext>(o =>
            o.UseInMemoryDatabase("census-reg-" + Guid.NewGuid())
        );
        return services;
    }
}

// ── Fixtures ────────────────────────────────────────────────────────────

public abstract class CensusEntityBase
{
    public string InheritedNote { get; set; } = string.Empty;
}

[TraxQueryModel]
[TraxAllowAnonymous]
public class PublicThing : CensusEntityBase
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[TraxQueryModel]
[TraxAuthorize(Roles = "admin")]
public class GatedThing
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CensusDbContext(DbContextOptions<CensusDbContext> options) : DbContext(options)
{
    public DbSet<PublicThing> PublicThings => Set<PublicThing>();
    public DbSet<GatedThing> GatedThings => Set<GatedThing>();
}

/// <summary>
/// Holds no <c>[TraxAllowAnonymous]</c> entity, so it can be used under a gated endpoint.
/// </summary>
public class GatedOnlyDbContext(DbContextOptions<GatedOnlyDbContext> options) : DbContext(options)
{
    public DbSet<GatedThing> GatedThings => Set<GatedThing>();
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class UndeclaredOnPublicThing
{
    public string Undeclared([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class SecondUndeclaredOnPublicThing
{
    public string SecondUndeclared([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class GatedOnPublicThing
{
    [TraxAuthorize(Roles = "subscriber")]
    public string Gated([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class AnonymousOnPublicThing
{
    [TraxAllowAnonymous]
    public string Anonymous([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class ConflictedOnPublicThing
{
    [TraxAuthorize]
    [TraxAllowAnonymous]
    public string Conflicted([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
[TraxAuthorize(Roles = "subscriber")]
public sealed class ClassGatedOnPublicThing
{
    public string ClassGated([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class ForeignGatedOnPublicThing
{
    [HotChocolate.Authorization.Authorize(Roles = ["subscriber"])]
    public string ForeignGated([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class ForeignAnonymousOnPublicThing
{
    [HotChocolate.Authorization.AllowAnonymous]
    public string ForeignAnonymous([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(GatedThing))]
public sealed class ForeignGatedOnGatedThing
{
    [HotChocolate.Authorization.Authorize]
    public string ForeignOnGated([Parent] GatedThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class UndeclaredViaConfigureSchema
{
    public string ViaConfigureSchema([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(GatedThing))]
public sealed class UndeclaredOnGatedThing
{
    public string Undeclared([Parent] GatedThing thing) => thing.Name;
}

[ExtendObjectType("RootQuery")]
public sealed class UndeclaredOnRootQuery
{
    public string UndeclaredRootQueryField() => "open";
}

/// <summary>
/// Targets RootMutation rather than RootQuery on purpose. A class-level attribute lands on the
/// type being extended, so on RootQuery it would gate every root field of every host in this
/// assembly that scans for type extensions. RootMutation exists only where a host asked for it.
/// </summary>
[ExtendObjectType("RootMutation")]
[TraxAuthorize]
public sealed class ClassGatedOnRootMutation
{
    public string ClassGatedRootField() => "gated";
}

[ExtendObjectType("RootQuery")]
public sealed class ConflictedOnRootQuery
{
    [TraxAuthorize]
    [TraxAllowAnonymous]
    public string ConflictedRootField() => "conflicted";
}

[ExtendObjectType("RootMutation")]
public sealed class UndeclaredOnRootMutation
{
    public bool UndeclaredRootMutationField() => true;
}

[ExtendObjectType("LifecycleSubscriptions")]
public sealed class UndeclaredSubscription
{
    [Subscribe(With = nameof(SubscribeAsync))]
    public string UndeclaredStream([EventMessage] string message) => message;

    public async IAsyncEnumerable<string> SubscribeAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}

[ExtendObjectType("LifecycleSubscriptions")]
public sealed class AnonymousSubscription
{
    [TraxAllowAnonymous]
    [Subscribe(With = nameof(SubscribeAsync))]
    public string AnonymousStream([EventMessage] string message) => message;

    public async IAsyncEnumerable<string> SubscribeAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}

[ExtendObjectType(typeof(Trax.Api.GraphQL.Mutations.OperationsMutations))]
public sealed class UndeclaredOnOperationsMutations
{
    public bool UndeclaredOperationsField() => true;
}
