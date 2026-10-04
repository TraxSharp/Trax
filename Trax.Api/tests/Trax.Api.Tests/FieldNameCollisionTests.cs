using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Two surfaces that would put a field of the same name on the same type, or generate a type of
/// the same name, refuse the host at startup naming both. HotChocolate merges two fields of one
/// name into one, the last resolver winning, so without the refusal one surface would silently
/// answer for the other. Guard for <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.
/// </summary>
[Property("adr", "docs/adr/0001-a-misconfigured-host-fails-at-startup.md")]
[TestFixture]
public class FieldNameCollisionTests
{
    private const string Adr =
        "docs/adr/0001-a-misconfigured-host-fails-at-startup.md: a collision is refused at "
        + "startup, naming both sides";

    [Test]
    public async Task AQueryTrainAndAQueryModelOnTheSameField_AreRefused()
    {
        var start = () =>
            PostureHost.StartAsync(
                g => g.AddDbContext<TeamContext>(),
                s => s.AddInMemoryContext<TeamContext>(),
                trains: [Train<ITeamsTrain>(query: true)]
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("'teams'")
            .And.Contain(typeof(ITeamsTrain).FullName!)
            .And.Contain(typeof(Team).FullName!);
    }

    [Test]
    public async Task OneEntityExposedByTwoContexts_IsRefused()
    {
        var start = () =>
            PostureHost.StartAsync(
                g => g.AddDbContext<TeamContext>().AddDbContext<SecondTeamContext>(),
                s =>
                {
                    s.AddInMemoryContext<TeamContext>();
                    s.AddInMemoryContext<SecondTeamContext>();
                }
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("'teams'")
            .And.Contain(typeof(TeamContext).FullName!)
            .And.Contain(typeof(SecondTeamContext).FullName!);
    }

    [Test]
    public async Task ANamespaceAndAQueryModelOnTheSameField_AreRefused()
    {
        var start = () =>
            PostureHost.StartAsync(
                g => g.AddDbContext<TeamContext>(),
                s => s.AddInMemoryContext<TeamContext>(),
                trains: [Train<IRosterTrain>(query: true, ns: "teams")]
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("'teams'")
            .And.Contain("namespace 'teams'")
            .And.Contain(typeof(Team).FullName!);
    }

    /// <summary>
    /// <c>IFooTrain</c>'s output class is called <c>FooResponse</c>, so its response type falls
    /// back to <c>FooMutationResponse</c>, the name <c>IFooMutationTrain</c>'s response takes.
    /// </summary>
    [Test]
    public async Task TwoTrainsGeneratingTheSameResponseType_AreRefused()
    {
        var start = () =>
            PostureHost.StartAsync(
                g => g.ExposeOperationQueries().AllowAnonymousOperations(),
                trains: [Train<IFooTrain>(output: typeof(FooResponse)), Train<IFooMutationTrain>()]
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("'FooMutationResponse'")
            .And.Contain(typeof(IFooTrain).FullName!)
            .And.Contain(typeof(IFooMutationTrain).FullName!);
    }

    /// <summary>
    /// The output type is named by its <c>[GraphQLName]</c>, not its CLR name, so the response
    /// type steps aside for it.
    /// </summary>
    [Test]
    public async Task AnOutputTypeRenamedToTheResponseName_PushesTheResponseAside()
    {
        using var host = await PostureHost.StartAsync(
            g => g.ExposeOperationQueries().AllowAnonymousOperations(),
            trains: [Train<IBarTrain>(output: typeof(BarResult))]
        );

        var executor = await host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        executor.Schema.Types.Select(t => t.Name).Should().Contain("BarMutationResponse");
        executor
            .Schema.Types.OfType<ObjectType>()
            .Single(t => t.Name == "BarResponse")
            .RuntimeType.Should()
            .Be<BarResult>();
    }

    [Test]
    public async Task ATypeExtensionFieldOnAQueryModelsField_IsRefused()
    {
        var start = () =>
            PostureHost.StartAsync(
                g =>
                    g.AddDbContext<TeamContext>()
                        .ConfigureSchema(b => b.AddTypeExtension<TeamsOnDiscover>()),
                s => s.AddInMemoryContext<TeamContext>()
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("DiscoverQueries.teams")
            .And.Contain(typeof(CollisionResolvers).FullName!)
            .And.Contain(typeof(Team).FullName!);
    }

    /// <summary>An extension that names its target by CLR type is checked the same way.</summary>
    [Test]
    public async Task AnExtensionTargetingTheNamespaceByType_IsRefusedToo()
    {
        var start = () =>
            PostureHost.StartAsync(
                g =>
                    g.AddDbContext<TeamContext>()
                        .ConfigureSchema(b => b.AddTypeExtension<TeamsOnDiscoverByType>()),
                s => s.AddInMemoryContext<TeamContext>()
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should().Contain("DiscoverQueries.teams").And.Contain(typeof(Team).FullName!);
    }

    /// <summary>
    /// A field the extension ignores is an instruction to drop it, not a field of its own, so it
    /// claims no name: ignoring <c>operations</c> does not collide with Trax's.
    /// </summary>
    [Test]
    public async Task AnIgnoredExtensionField_ClaimsNoName()
    {
        var start = () =>
            PostureHost.StartAsync(g =>
                g.ExposeOperationQueries()
                    .AllowAnonymousOperations()
                    .ConfigureSchema(b => b.AddTypeExtension<IgnoredFieldOnRoot>())
            );

        await start.Should().NotThrowAsync();
    }

    [Test]
    public async Task TwoTypeExtensionsOnTheSameField_AreRefused()
    {
        var start = () =>
            PostureHost.StartAsync(g =>
                g.ExposeOperationQueries()
                    .AllowAnonymousOperations()
                    .ConfigureSchema(b =>
                        b.AddTypeExtension<FirstDuplicate>().AddTypeExtension<SecondDuplicate>()
                    )
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("RootQuery.duplicated")
            .And.Contain(typeof(CollisionResolvers).FullName!)
            .And.Contain(typeof(OtherCollisionResolvers).FullName!);
    }

    [Test]
    public async Task ATypeExtensionFieldOnTheOperationsRootField_IsRefused()
    {
        var start = () =>
            PostureHost.StartAsync(g =>
                g.ExposeOperationQueries()
                    .AllowAnonymousOperations()
                    .ConfigureSchema(b => b.AddTypeExtension<OperationsOnRoot>())
            );

        var ex = (await start.Should().ThrowAsync<InvalidOperationException>(Adr)).Which;
        ex.Message.Should()
            .Contain("RootQuery.operations")
            .And.Contain(typeof(CollisionResolvers).FullName!);
    }

    [Test]
    public async Task DistinctNamesEverywhere_Start()
    {
        using var host = await PostureHost.StartAsync(
            g => g.AddDbContext<TeamContext>(),
            s => s.AddInMemoryContext<TeamContext>(),
            trains: [Train<IRosterTrain>(query: true, ns: "rosters"), Train<IFooTrain>()]
        );
    }

    private static TrainRegistration Train<T>(
        bool query = false,
        string? ns = null,
        Type? output = null
    ) =>
        new()
        {
            ServiceType = typeof(T),
            ImplementationType = typeof(T),
            InputType = typeof(CollisionInput),
            OutputType = output ?? typeof(CollisionOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(T).Name,
            ImplementationTypeName = typeof(T).Name,
            InputTypeName = nameof(CollisionInput),
            OutputTypeName = (output ?? typeof(CollisionOutput)).Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAuthorizeAttribute = false,
            HasAllowAnonymousAttribute = true,
            IsQuery = query,
            IsMutation = !query,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLNamespace = ns,
            GraphQLOperations = GraphQLOperation.Run,
        };

    public interface ITeamsTrain;

    public interface IRosterTrain;

    public interface IFooTrain;

    public interface IFooMutationTrain;

    public interface IBarTrain;

    public sealed record CollisionInput
    {
        public string Value { get; init; } = "";
    }

    public sealed record CollisionOutput
    {
        public string Result { get; init; } = "";
    }

    public sealed record FooResponse
    {
        public string Result { get; init; } = "";
    }

    [GraphQLName("BarResponse")]
    public sealed record BarResult
    {
        public string Result { get; init; } = "";
    }

    [TraxQueryModel]
    [TraxAllowAnonymous]
    public sealed class Team
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    public sealed class TeamContext(DbContextOptions<TeamContext> options) : DbContext(options)
    {
        public DbSet<Team> Teams => Set<Team>();
    }

    public sealed class SecondTeamContext(DbContextOptions<SecondTeamContext> options)
        : DbContext(options)
    {
        public DbSet<Team> Teams => Set<Team>();
    }

    // The extensions below are built with descriptors rather than [ExtendObjectType], so a test
    // that scans this assembly for type extensions does not pick them up.

    public sealed class TeamsOnDiscover : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("DiscoverQueries");
            descriptor.Field<CollisionResolvers>(r => r.Teams());
        }
    }

    /// <summary>
    /// Names its target by CLR type. A scan of this assembly picks it up, which is harmless: no
    /// host the scan builds has a <c>teams</c> field of its own.
    /// </summary>
    [ExtendObjectType(typeof(Trax.Api.GraphQL.Queries.DiscoverQueries))]
    public sealed class TeamsOnDiscoverByType
    {
        [TraxAllowAnonymous]
        public string Teams() => "by-type";
    }

    public sealed class IgnoredFieldOnRoot : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<CollisionResolvers>(r => r.Operations()).Ignore();
            descriptor.Field<OtherCollisionResolvers>(r => r.Duplicated());
        }
    }

    public sealed class FirstDuplicate : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<CollisionResolvers>(r => r.Duplicated());
        }
    }

    public sealed class SecondDuplicate : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<OtherCollisionResolvers>(r => r.Duplicated());
        }
    }

    public sealed class OperationsOnRoot : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<CollisionResolvers>(r => r.Operations());
        }
    }

    public sealed class CollisionResolvers
    {
        [TraxAllowAnonymous]
        public string Teams() => "extension";

        [TraxAllowAnonymous]
        public string Duplicated() => "first";

        [TraxAllowAnonymous]
        public string Operations() => "shadow";
    }

    public sealed class OtherCollisionResolvers
    {
        [TraxAllowAnonymous]
        public string Duplicated() => "second";
    }
}
