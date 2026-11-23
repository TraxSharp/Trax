using System.Globalization;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Covers the contract between <c>[TraxQueryModel]</c> projection and hand-written
/// <c>[ExtendObjectType]</c> resolvers that read a property off their <c>[Parent]</c>.
///
/// <para>
/// Projection narrows the SELECT to the columns the caller named. A resolver that reads
/// a property nobody selected therefore gets a default value — <c>0</c> for an int, null
/// for a string — and silently returns an empty or zero answer. Trax closes that by
/// declaring the entity key as a projection requirement on every field that is not backed
/// by an entity property, and by honouring <c>[Parent(requires:)]</c> for anything else.
/// </para>
///
/// <para>
/// <see cref="EchoName_NotSelectedAndNotRequired_IsUnset"/> is the guard against the
/// opposite failure: if projection ever stopped narrowing, every other test here would
/// pass vacuously because the resolver would receive a fully-materialised entity.
/// </para>
/// </summary>
[TestFixture]
public class QueryModelProjectionTests
{
    private const string Unset = "<unset>";

    #region Entity key is required automatically

    [Test]
    public async Task EchoId_KeyNotSelected_ResolverStillReceivesTheKey()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoId");

        // Without the requirement the resolver sees Id = 0 and echoes 0.
        nodes
            .Select(n =>
                Convert.ToInt32(n["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Equal(1, 2);
    }

    [Test]
    public async Task EchoId_KeySelected_ResolverReceivesTheKey()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "id echoId");

        nodes
            .Select(n =>
                Convert.ToInt32(n["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Equal(1, 2);
        nodes
            .Select(n =>
                Convert.ToInt32(n["id"], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Equal(1, 2);
    }

    [Test]
    public async Task EchoId_CustomKeyAttribute_ResolverReceivesTheKey()
    {
        // Sprocket's key is [Key] Code, not a property named Id. The requirement must
        // follow the declared key, not the Id convention.
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "sprockets", "label echoCode");

        nodes.Select(n => (string?)n["echoCode"]).Should().Equal("SP-1", "SP-2");
    }

    [Test]
    public async Task EchoCompositeKey_BothKeyPartsRequired_ResolverReceivesBoth()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "pairs", "note echoKey");

        nodes.Select(n => (string?)n["echoKey"]).Should().Equal("1/10", "2/20");
    }

    #endregion

    #region [Parent(requires:)] covers non-key properties

    [Test]
    public async Task EchoGadgetId_DeclaredViaRequires_ResolverReceivesTheForeignKey()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoGadgetId");

        nodes.Select(n => (string?)n["echoGadgetId"]).Should().Equal("101", "202");
    }

    [Test]
    public async Task EchoNameRequired_DeclaredViaRequires_ResolverReceivesTheColumn()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoNameRequired");

        nodes.Select(n => (string?)n["echoNameRequired"]).Should().Equal("Alpha", "Beta");
    }

    [Test]
    public async Task LambdaResolvedField_HasNoResolverMethod_StillGetsTheKey()
    {
        // A field added through ConfigureSchema resolves from a delegate, so there is no
        // method to read a [Parent(requires:)] declaration off. Trax still has to pin the
        // key, because the delegate reads the parent just like a method resolver would.
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug lambdaEchoId");

        nodes
            .Select(n =>
                Convert.ToInt32(
                    n["lambdaEchoId"],
                    System.Globalization.CultureInfo.InvariantCulture
                )
            )
            .Should()
            .Equal(1, 2);
    }

    [Test]
    public async Task EchoNameAndId_DeclaredColumnAndKey_BothArrive()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoNameAndId");

        // Replacing rather than merging would drop one side and produce "<unset>/1" or
        // "Alpha/0".
        nodes.Select(n => (string?)n["echoNameAndId"]).Should().Equal("Alpha/1", "Beta/2");
    }

    #endregion

    #region Projection still narrows

    [Test]
    public async Task EchoName_NotSelectedAndNotRequired_IsUnset()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoName");

        // Name is neither selected nor required, so projection must leave it unset.
        // If this ever returns "Alpha"/"Beta", projection stopped narrowing and every
        // other assertion in this fixture became meaningless.
        nodes.Select(n => (string?)n["echoName"]).Should().Equal(Unset, Unset);
    }

    [Test]
    public async Task EchoName_ColumnSelectedByCaller_ResolverReceivesIt()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "name echoName");

        nodes.Select(n => (string?)n["echoName"]).Should().Equal("Alpha", "Beta");
    }

    [Test]
    public async Task ProjectionDisabled_ResolverReceivesTheWholeEntity()
    {
        // [TraxQueryModel(Projection = false)] means no Select at all, so every column
        // is materialised and no requirement machinery is involved.
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "cogs", "slug echoName echoId");

        nodes.Select(n => (string?)n["echoName"]).Should().Equal("Alpha", "Beta");
        nodes
            .Select(n =>
                Convert.ToInt32(n["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Equal(1, 2);
    }

    #endregion

    #region Composition with filtering, sorting, paging

    [Test]
    public async Task Requirement_ComposesWithFilterOnUnselectedColumn()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(
            executor,
            "widgets",
            "slug echoId",
            "(where: { name: { eq: \"Beta\" } })"
        );

        nodes.Should().ContainSingle();
        Convert
            .ToInt32(nodes[0]["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .Be(2);
    }

    [Test]
    public async Task Requirement_ComposesWithSortOnUnselectedColumn()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(
            executor,
            "widgets",
            "slug echoId",
            "(order: [{ name: DESC }])"
        );

        nodes
            .Select(n =>
                Convert.ToInt32(n["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            )
            .Should()
            .Equal(2, 1);
    }

    [Test]
    public async Task TotalCountOnly_NoNodeSelection_Succeeds()
    {
        // The selection set names no node field at all. The projection step must not
        // blow up on an empty selector.
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync("{ discover { widgets { totalCount } } }");

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"].NotNull();
        var widgets = (IReadOnlyDictionary<string, object?>)discover["widgets"].NotNull();
        Convert
            .ToInt32(widgets["totalCount"], System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .Be(2);
    }

    [Test]
    public async Task Requirement_ComposesWithPaging()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoId", "(first: 1)");

        nodes.Should().ContainSingle();
        Convert
            .ToInt32(nodes[0]["echoId"], System.Globalization.CultureInfo.InvariantCulture)
            .Should()
            .Be(1);
    }

    [Test]
    public async Task Requirement_AppliesInsideEdgesNodeSelection()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync(
            "{ discover { widgets { edges { node { slug echoId } } } } }"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"].NotNull();
        var widgets = (IReadOnlyDictionary<string, object?>)discover["widgets"].NotNull();
        var edges = (IReadOnlyList<object?>)widgets["edges"].NotNull();

        edges
            .Select(e =>
            {
                var node =
                    (IReadOnlyDictionary<string, object?>)
                        ((IReadOnlyDictionary<string, object?>)e.NotNull())["node"].NotNull();
                return Convert.ToInt32(
                    node["echoId"],
                    System.Globalization.CultureInfo.InvariantCulture
                );
            })
            .Should()
            .Equal(1, 2);
    }

    #endregion

    #region Entity navigations are unaffected

    [Test]
    public async Task Navigation_StillProjectsThroughTheJoin()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug gadget { title }");

        nodes
            .Select(n =>
                (string?)((IReadOnlyDictionary<string, object?>)n["gadget"].NotNull())["title"]
            )
            .Should()
            .Equal("G-Alpha", "G-Beta");
    }

    #endregion

    #region Helpers

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NodesAsync(
        IRequestExecutor executor,
        string field,
        string selection,
        string arguments = ""
    )
    {
        var result = await executor
            .ExecuteAsync($"{{ discover {{ {field}{arguments} {{ nodes {{ {selection} }} }} }} }}")
            .ConfigureAwait(false);

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"].NotNull();
        var connection = (IReadOnlyDictionary<string, object?>)discover[field].NotNull();
        var nodes = (IReadOnlyList<object?>)connection["nodes"].NotNull();

        return [.. nodes.Cast<IReadOnlyDictionary<string, object?>>()];
    }

    private static async Task<IRequestExecutor> BuildExecutorAsync()
    {
        var dbName = "ProjectionTest_" + Guid.NewGuid();
        var dbRoot = new InMemoryDatabaseRoot();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<ProjectionDbContext>(o => o.UseInMemoryDatabase(dbName, dbRoot));

        services.AddTraxGraphQL(g =>
            g.AddDbContext<ProjectionDbContext>()
                .AddTypeExtension<WidgetProbeExtension>()
                .AddTypeExtension<CogProbeExtension>()
                .AddTypeExtension<SprocketProbeExtension>()
                .AddTypeExtension<PairProbeExtension>()
                .ConfigureSchema(schema =>
                    schema.AddTypeExtension(
                        new ObjectTypeExtension(d =>
                        {
                            d.Name(nameof(Widget));
                            d.Field("lambdaEchoId")
                                .Type<IntType>()
                                .Resolve(ctx => ctx.Parent<Widget>().Id);
                        })
                    )
                )
        );

        var provider = services.BuildServiceProvider();

        var scope = provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectionDbContext>();
            AddSeedRows(db);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        return await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .ConfigureAwait(false);
    }

    private static void AddSeedRows(ProjectionDbContext db)
    {
        db.Gadgets.AddRange(
            new Gadget { Id = 101, Title = "G-Alpha" },
            new Gadget { Id = 202, Title = "G-Beta" }
        );
        db.Widgets.AddRange(
            new Widget
            {
                Id = 1,
                Slug = "a",
                Name = "Alpha",
                GadgetId = 101,
            },
            new Widget
            {
                Id = 2,
                Slug = "b",
                Name = "Beta",
                GadgetId = 202,
            }
        );
        db.Cogs.AddRange(
            new Cog
            {
                Id = 1,
                Slug = "a",
                Name = "Alpha",
            },
            new Cog
            {
                Id = 2,
                Slug = "b",
                Name = "Beta",
            }
        );
        db.Sprockets.AddRange(
            new Sprocket { Code = "SP-1", Label = "one" },
            new Sprocket { Code = "SP-2", Label = "two" }
        );
        db.Pairs.AddRange(
            new Pair
            {
                Left = 1,
                Right = 10,
                Note = "first",
            },
            new Pair
            {
                Left = 2,
                Right = 20,
                Note = "second",
            }
        );
    }

    #endregion
}
