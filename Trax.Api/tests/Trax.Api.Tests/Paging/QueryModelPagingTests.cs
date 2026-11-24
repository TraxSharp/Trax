using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Paging;

/// <summary>
/// The paging styles a query model can take: cursor, offset, none, and the schema default the
/// host sets with <c>DefaultPaging</c>.
/// </summary>
[TestFixture]
public class QueryModelPagingTests
{
    private static readonly string[] Names = ["alder", "birch", "beech", "cedar", "bay"];

    #region Offset

    [Test]
    public async Task Offset_SkipAndTake_ReturnsTheSliceWithPageInfoAndTotalCount()
    {
        var executor = await BuildExecutorAsync();

        using var doc = await ExecuteAsync(
            executor,
            """
            { discover { ledgers(skip: 1, take: 2, order: [{ id: ASC }]) {
                items { id }
                pageInfo { hasNextPage hasPreviousPage }
                totalCount
            } } }
            """
        );

        var ledgers = Field(doc, "ledgers");
        Ids(ledgers.GetProperty("items")).Should().Equal(2, 3);
        ledgers.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        ledgers
            .GetProperty("pageInfo")
            .GetProperty("hasPreviousPage")
            .GetBoolean()
            .Should()
            .BeTrue();
        ledgers.GetProperty("totalCount").GetInt32().Should().Be(5);
    }

    [Test]
    public async Task Offset_WhereAndOrder_MatchTheCursorField()
    {
        var executor = await BuildExecutorAsync();

        using var offset = await ExecuteAsync(
            executor,
            """
            { discover { ledgers(take: 50, where: { name: { startsWith: "b" } }, order: [{ name: DESC }]) {
                items { name }
                totalCount
            } } }
            """
        );
        using var cursor = await ExecuteAsync(
            executor,
            """
            { discover { journals(first: 50, where: { name: { startsWith: "b" } }, order: [{ name: DESC }]) {
                nodes { name }
                totalCount
            } } }
            """
        );

        var offsetNames = NamesOf(Field(offset, "ledgers").GetProperty("items"));
        offsetNames.Should().Equal("birch", "beech", "bay");
        offsetNames.Should().Equal(NamesOf(Field(cursor, "journals").GetProperty("nodes")));
        Field(offset, "ledgers").GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task Offset_SkipAtTheCap_IsAccepted()
    {
        var executor = await BuildExecutorAsync(g => g.MaxOffsetSkip(4));

        using var doc = await ExecuteAsync(
            executor,
            "{ discover { ledgers(skip: 4, order: [{ id: ASC }]) { items { id } } } }"
        );

        Ids(Field(doc, "ledgers").GetProperty("items")).Should().Equal(5);
    }

    [Test]
    public async Task Offset_SkipAboveTheCap_IsRefused()
    {
        var executor = await BuildExecutorAsync(g => g.MaxOffsetSkip(4));

        var result = (
            await executor.ExecuteAsync("{ discover { ledgers(skip: 5) { items { id } } } }")
        ).ExpectOperationResult();

        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("TRAX_OFFSET_SKIP_LIMIT");
    }

    [Test]
    public async Task Offset_DefaultCap_RefusesALargeSkip()
    {
        var executor = await BuildExecutorAsync();

        var result = (
            await executor.ExecuteAsync(
                $"{{ discover {{ ledgers(skip: {TraxGraphQLBuilder.DefaultMaxOffsetSkip + 1}) {{ totalCount }} }} }}"
            )
        ).ExpectOperationResult();

        result.Errors.Should().ContainSingle();
        result.Errors[0].Code.Should().Be("TRAX_OFFSET_SKIP_LIMIT");
    }

    [Test]
    public async Task Offset_TakeAboveTheMaxPageSize_IsRefused()
    {
        var executor = await BuildExecutorAsync();

        var result = (
            await executor.ExecuteAsync("{ discover { ledgers(take: 51) { items { id } } } }")
        ).ExpectOperationResult();

        result.Errors.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Default style

    [Test]
    public async Task NoSetting_DefaultModelIsCursorPaged()
    {
        var executor = await BuildExecutorAsync();

        using var doc = await ExecuteAsync(
            executor,
            "{ discover { notebooks(first: 2, order: [{ id: ASC }]) { nodes { id } pageInfo { endCursor } } } }"
        );

        Ids(Field(doc, "notebooks").GetProperty("nodes")).Should().Equal(1, 2);
    }

    [Test]
    public async Task DefaultPagingOffset_ChangesOnlyModelsThatLeaveItAtDefault()
    {
        var executor = await BuildExecutorAsync(g => g.DefaultPaging(TraxPaging.Offset));

        using var doc = await ExecuteAsync(
            executor,
            """
            { discover {
                notebooks(skip: 3, order: [{ id: ASC }]) { items { id } totalCount }
                journals(first: 1, order: [{ id: ASC }]) { nodes { id } }
                pamphlets(order: [{ id: ASC }]) { id }
            } }
            """
        );

        Ids(Field(doc, "notebooks").GetProperty("items")).Should().Equal(4, 5);
        Ids(Field(doc, "journals").GetProperty("nodes")).Should().Equal(1);
        Ids(Field(doc, "pamphlets")).Should().Equal(1, 2, 3, 4, 5);
    }

    [Test]
    public async Task DefaultPagingNone_MakesDefaultModelsPlainLists()
    {
        var executor = await BuildExecutorAsync(g => g.DefaultPaging(TraxPaging.None));

        using var doc = await ExecuteAsync(
            executor,
            "{ discover { notebooks(order: [{ id: ASC }]) { id } } }"
        );

        Ids(Field(doc, "notebooks")).Should().Equal(1, 2, 3, 4, 5);
    }

    [Test]
    public void DefaultPaging_Default_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var act = () => builder.DefaultPaging(TraxPaging.Default);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Build_UndefinedPagingValue_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<UndefinedPagingDbContext>();

        var act = () => builder.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*PagedScroll*Paging = 99*Default, None, Cursor or Offset*");
    }

    [Test]
    public void MaxOffsetSkip_Negative_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var act = () => builder.MaxOffsetSkip(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Helpers

    private static async Task<JsonDocument> ExecuteAsync(IRequestExecutor executor, string query)
    {
        var result = (
            await executor.ExecuteAsync(query).ConfigureAwait(false)
        ).ExpectOperationResult();
        result.Errors.Should().BeNullOrEmpty();
        return JsonDocument.Parse(result.ToJson());
    }

    private static JsonElement Field(JsonDocument doc, string field) =>
        doc.RootElement.GetProperty("data").GetProperty("discover").GetProperty(field);

    private static int[] Ids(JsonElement list) =>
        [.. list.EnumerateArray().Select(e => e.GetProperty("id").GetInt32())];

    private static string?[] NamesOf(JsonElement list) =>
        [.. list.EnumerateArray().Select(e => e.GetProperty("name").GetString())];

    private static async Task<IRequestExecutor> BuildExecutorAsync(
        Action<TraxGraphQLBuilder>? configure = null
    )
    {
        var dbName = "PagingTest_" + Guid.NewGuid();
        var dbRoot = new InMemoryDatabaseRoot();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddDbContext<PagingDbContext>(o => o.UseInMemoryDatabase(dbName, dbRoot));

        services.AddTraxGraphQL(g =>
        {
            g.AddDbContext<PagingDbContext>();
            configure?.Invoke(g);
            return g;
        });

        var provider = services.BuildServiceProvider();

        var scope = provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<PagingDbContext>();
            for (var i = 0; i < Names.Length; i++)
            {
                db.Ledgers.Add(new PagedLedger { Id = i + 1, Name = Names[i] });
                db.Journals.Add(new PagedJournal { Id = i + 1, Name = Names[i] });
                db.Notebooks.Add(new PagedNotebook { Id = i + 1, Name = Names[i] });
                db.Pamphlets.Add(new PagedPamphlet { Id = i + 1, Name = Names[i] });
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        return await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .ConfigureAwait(false);
    }

    #endregion
}
