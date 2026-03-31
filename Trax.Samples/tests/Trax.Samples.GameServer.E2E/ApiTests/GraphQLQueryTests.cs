using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class GraphQLQueryTests : ApiTestFixture
{
    [Test]
    public async Task LookupPlayer_ReturnsPlayerProfile()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        lookupPlayer(input: { playerId: "player-1" }) {
                            playerId
                            rank
                            wins
                            losses
                            rating
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var player = result.GetData("discover", "players", "lookupPlayer");
        player.GetProperty("playerId").GetString().Should().Be("player-1");
        player.GetProperty("rank").GetInt32().Should().BeGreaterThan(0);
        player.GetProperty("wins").GetInt32().Should().BeGreaterThan(0);
        player.GetProperty("rating").GetInt32().Should().BeGreaterThan(0);

        // Verify the train actually executed through the full pipeline and persisted metadata.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "LookupPlayer",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
        metadata.Input.Should().Contain("player-1");
    }

    [Test]
    public async Task PlayerRecords_QueryModel_ReturnsPaginatedData()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(first: 3) {
                            nodes {
                                playerId
                                displayName
                                rating
                            }
                            pageInfo {
                                hasNextPage
                            }
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var nodes = result.GetData("discover", "players", "playerRecords", "nodes");
        nodes.GetArrayLength().Should().Be(3);

        var pageInfo = result.GetData("discover", "players", "playerRecords", "pageInfo");
        pageInfo.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();

        // PlayerRecords is a HotChocolate query model that resolves directly against
        // IQueryable<PlayerRecord> — no train execution, so no metadata to verify.
        // The pagination response itself proves the data layer was queried correctly.
    }
}
