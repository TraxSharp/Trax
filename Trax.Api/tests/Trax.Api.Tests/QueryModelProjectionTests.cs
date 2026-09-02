using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
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
        nodes.Select(n => Convert.ToInt32(n["echoId"])).Should().Equal(1, 2);
    }

    [Test]
    public async Task EchoId_KeySelected_ResolverReceivesTheKey()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "id echoId");

        nodes.Select(n => Convert.ToInt32(n["echoId"])).Should().Equal(1, 2);
        nodes.Select(n => Convert.ToInt32(n["id"])).Should().Equal(1, 2);
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

        nodes.Select(n => Convert.ToInt32(n["lambdaEchoId"])).Should().Equal(1, 2);
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
        nodes.Select(n => Convert.ToInt32(n["echoId"])).Should().Equal(1, 2);
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
        Convert.ToInt32(nodes[0]["echoId"]).Should().Be(2);
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

        nodes.Select(n => Convert.ToInt32(n["echoId"])).Should().Equal(2, 1);
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

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"]!;
        var widgets = (IReadOnlyDictionary<string, object?>)discover["widgets"]!;
        Convert.ToInt32(widgets["totalCount"]).Should().Be(2);
    }

    [Test]
    public async Task Requirement_ComposesWithPaging()
    {
        var executor = await BuildExecutorAsync();

        var nodes = await NodesAsync(executor, "widgets", "slug echoId", "(first: 1)");

        nodes.Should().ContainSingle();
        Convert.ToInt32(nodes[0]["echoId"]).Should().Be(1);
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

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"]!;
        var widgets = (IReadOnlyDictionary<string, object?>)discover["widgets"]!;
        var edges = (IReadOnlyList<object?>)widgets["edges"]!;

        edges
            .Select(e =>
            {
                var node =
                    (IReadOnlyDictionary<string, object?>)
                        ((IReadOnlyDictionary<string, object?>)e!)["node"]!;
                return Convert.ToInt32(node["echoId"]);
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
            .Select(n => (string?)((IReadOnlyDictionary<string, object?>)n["gadget"]!)["title"])
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
        var result = await executor.ExecuteAsync(
            $"{{ discover {{ {field}{arguments} {{ nodes {{ {selection} }} }} }} }}"
        );

        var op = result.ExpectOperationResult();
        op.Errors.Should().BeNullOrEmpty();

        var discover = (IReadOnlyDictionary<string, object?>)op.DataMap()["discover"]!;
        var connection = (IReadOnlyDictionary<string, object?>)discover[field]!;
        var nodes = (IReadOnlyList<object?>)connection["nodes"]!;

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

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProjectionDbContext>();
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
            await db.SaveChangesAsync();
        }

        return await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    #endregion
}

#region Test entities, extensions and context

[TraxAllowAnonymous]
[TraxQueryModel(Name = "widgets")]
public class Widget
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int? GadgetId { get; set; }
    public Gadget? Gadget { get; set; }
}

[TraxAllowAnonymous]
[TraxQueryModel(Name = "gadgets")]
public class Gadget
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Projection off: the resolver always sees a fully materialised entity.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Name = "cogs", Projection = false)]
public class Cog
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Key declared with <c>[Key]</c> on a property that is not named <c>Id</c>.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Name = "sprockets")]
public class Sprocket
{
    [System.ComponentModel.DataAnnotations.Key]
    public string Code { get; set; } = "";

    public string Label { get; set; } = "";
}

/// <summary>Composite key: both parts must land in the projection.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Name = "pairs")]
public class Pair
{
    [System.ComponentModel.DataAnnotations.Key]
    [System.ComponentModel.DataAnnotations.Schema.Column(Order = 0)]
    public int Left { get; set; }

    [System.ComponentModel.DataAnnotations.Key]
    [System.ComponentModel.DataAnnotations.Schema.Column(Order = 1)]
    public int Right { get; set; }

    public string Note { get; set; } = "";
}

[ExtendObjectType(typeof(Widget))]
public sealed class WidgetProbeExtension
{
    /// <summary>Reads the entity key. Trax must supply it with no annotation here.</summary>
    public int GetEchoId([Parent] Widget widget) => widget.Id;

    /// <summary>Reads a non-key column, declared through HotChocolate's own mechanism.</summary>
    public string GetEchoGadgetId([Parent(requires: nameof(Widget.GadgetId))] Widget widget) =>
        widget.GadgetId?.ToString() ?? "<unset>";

    /// <summary>Reads a non-key column with no declaration: must stay unset.</summary>
    public string GetEchoName([Parent] Widget widget) =>
        string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name;

    /// <summary>Same column, declared: must arrive.</summary>
    public string GetEchoNameRequired([Parent(requires: nameof(Widget.Name))] Widget widget) =>
        string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name;

    /// <summary>
    /// Declares one column and also reads the key. Trax must merge its automatic key
    /// requirement with the declared one instead of replacing either.
    /// </summary>
    public string GetEchoNameAndId([Parent(requires: nameof(Widget.Name))] Widget widget) =>
        $"{(string.IsNullOrEmpty(widget.Name) ? "<unset>" : widget.Name)}/{widget.Id}";
}

[ExtendObjectType(typeof(Cog))]
public sealed class CogProbeExtension
{
    public int GetEchoId([Parent] Cog cog) => cog.Id;

    public string GetEchoName([Parent] Cog cog) =>
        string.IsNullOrEmpty(cog.Name) ? "<unset>" : cog.Name;
}

[ExtendObjectType(typeof(Sprocket))]
public sealed class SprocketProbeExtension
{
    public string GetEchoCode([Parent] Sprocket sprocket) =>
        string.IsNullOrEmpty(sprocket.Code) ? "<unset>" : sprocket.Code;
}

[ExtendObjectType(typeof(Pair))]
public sealed class PairProbeExtension
{
    public string GetEchoKey([Parent] Pair pair) => $"{pair.Left}/{pair.Right}";
}

public class ProjectionDbContext(DbContextOptions<ProjectionDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<Gadget> Gadgets => Set<Gadget>();
    public DbSet<Cog> Cogs => Set<Cog>();
    public DbSet<Sprocket> Sprockets => Set<Sprocket>();
    public DbSet<Pair> Pairs => Set<Pair>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pair>().HasKey(p => new { p.Left, p.Right });
    }
}

#endregion
