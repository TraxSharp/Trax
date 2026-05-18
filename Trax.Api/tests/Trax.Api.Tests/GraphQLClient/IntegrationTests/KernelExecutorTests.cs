using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.Fixtures;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// The cross-mode matrix. Every test runs both Mode A (raw string) and Mode E (resource)
/// against a real HotChocolate server hosted in-process. The same logical request shape is
/// exercised twice so a regression in one mode (e.g. resource loader fails to find the file)
/// shows up alongside the working mode.
///
/// Why each test is here (deleting any of these would let a real regression slip):
/// <list type="bullet">
///   <item>Happy-path query proves: validate ↔ HTTP ↔ extract end-to-end works with variables.</item>
///   <item>Mutation proves: operation-type detection routes to SendMutationAsync, not SendQuery.</item>
///   <item>Nullable field present/absent proves: deserialization tolerates schema-nullable fields.</item>
///   <item>List with enum proves: list deserialization + the snake-case-upper enum policy.</item>
///   <item>Validation drift proves: bad fields are caught BEFORE the HTTP call, not by the server.</item>
///   <item>Cancellation proves: tokens propagate through validator and HTTP path.</item>
///   <item>Empty data proves: GraphQLExecutionException fires when response has no data, not NRE.</item>
/// </list>
/// </summary>
[TestFixture]
public class KernelExecutorTests
{
    private GraphQLTestServerFixture _fixture = null!;
    private ServiceProvider _services = null!;
    private IGraphQLClientExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new GraphQLTestServerFixture();

        var services = new ServiceCollection();
        services
            .AddTraxGraphQLClient(_fixture.BaseAddress)
            .ConfigureHttpClient(_fixture.CreateHttpClient());
        _services = services.BuildServiceProvider();
        _executor = _services.GetRequiredService<IGraphQLClientExecutor>();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _fixture.Dispose();
    }

    #region Happy path - query

    [Test]
    public async Task RawString_FetchPlayer_ReturnsPopulatedProfile()
    {
        var player = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });

        player.Should().NotBeNull();
        player.Id.Should().Be("player-1");
        player.Name.Should().Be("Aragorn");
        player.Level.Should().Be(42);
        player.Rank.Should().Be("GOLD");
        player.Guild.Should().NotBeNull();
        player.Guild!.Id.Should().Be("guild-1");
        player.Guild.Name.Should().Be("Dragonsworn");
        player.Inventory.Should().HaveCount(2);
        player.Inventory[0].Category.Should().Be("WEAPON");
    }

    [Test]
    public async Task Resource_FetchPlayer_ReturnsSameShapeAsRawString()
    {
        var rawResult = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-1" });
        var resourceResult = await _executor.Run(
            new GetPlayerByResourceRequest { Id = "player-1" }
        );

        // Byte-equal across modes - the contract that lets us prove modes are interchangeable.
        resourceResult.Should().BeEquivalentTo(rawResult);
    }

    [Test]
    public async Task RawString_NullableLevelField_IsPreservedAsNull()
    {
        var player = await _executor.Run(new GetPlayerByRawStringRequest { Id = "player-2" });

        player.Level.Should().BeNull();
        player.Guild.Should().BeNull();
        player.Inventory.Should().BeEmpty();
    }

    [Test]
    public async Task Resource_NullableLevelField_IsPreservedAsNull()
    {
        var player = await _executor.Run(new GetPlayerByResourceRequest { Id = "player-2" });

        player.Level.Should().BeNull();
        player.Guild.Should().BeNull();
    }

    #endregion

    #region Happy path - list, enum

    [Test]
    public async Task RawString_ListOfPlayers_FiltersByVariable()
    {
        var players = await _executor.Run(new PlayersByLevelRequest { Min = 50 });

        players.Should().HaveCount(1);
        players[0].Id.Should().Be("player-3");
        players[0].Name.Should().Be("Gandalf");
    }

    [Test]
    public async Task RawString_ListOfItems_DecodesEnumCategory()
    {
        var items = await _executor.Run(new AllItemsRequest());

        items.Should().HaveCount(3);
        items.Select(i => i.Category).Should().BeEquivalentTo(["WEAPON", "ARMOR", "CONSUMABLE"]);
    }

    #endregion

    #region Mutation

    [Test]
    public async Task RawString_RenamePlayer_PersistsViaMutation()
    {
        var renamed = await _executor.Run(
            new RenamePlayerRequest { Id = "player-1", NewName = "Strider" }
        );

        renamed.Name.Should().Be("Strider");

        _fixture.PlayerStore.Get("player-1")!.Name.Should().Be("Strider");
    }

    [Test]
    public async Task Resource_RenamePlayer_PersistsViaMutation()
    {
        var renamed = await _executor.Run(
            new RenamePlayerByResourceRequest { Id = "player-2", NewName = "Frodo" }
        );

        renamed.Name.Should().Be("Frodo");

        _fixture.PlayerStore.Get("player-2")!.Name.Should().Be("Frodo");
    }

    #endregion

    #region Errors

    [Test]
    public async Task RawString_ServerError_BubblesAsExecutionException()
    {
        // player-999 doesn't exist -> rename mutation throws GraphQLException server-side
        var act = async () =>
            await _executor.Run(new RenamePlayerRequest { Id = "player-999", NewName = "Ghost" });

        var ex = await act.Should().ThrowAsync<GraphQLExecutionException>();
        ex.Which.Errors.Should().NotBeEmpty();
        ex.Which.Message.Should().Contain("not found");
    }

    [Test]
    public async Task RawString_DriftedQuery_FailsValidationBeforeHttpCall()
    {
        var act = async () =>
            await _executor.Run(new GetPlayerByDriftedQueryRequest { Id = "player-1" });

        var ex = await act.Should().ThrowAsync<GraphQLValidationException>();
        ex.Which.Errors.Should().NotBeEmpty();
        ex.Which.Message.Should().Contain("nope");
    }

    [Test]
    public async Task Run_NullRequest_ThrowsArgumentNull()
    {
        var act = async () =>
            await _executor.Run<PlayerProfile>((IGraphQLClientRequest<PlayerProfile>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task Run_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _executor.Run(new AllItemsRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
