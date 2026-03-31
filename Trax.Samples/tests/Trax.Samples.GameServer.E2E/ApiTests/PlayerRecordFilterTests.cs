using System.Text.Json;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class PlayerRecordFilterTests : ApiTestFixture
{
    [Test]
    public async Task PlayerRecords_FilterByRating_GTE()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(first: 10, where: { rating: { gte: 1800 } }) {
                            nodes {
                                playerId
                                rating
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
        nodes.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var node in nodes.EnumerateArray())
        {
            node.GetProperty("rating").GetInt32().Should().BeGreaterThanOrEqualTo(1800);
        }
    }

    [Test]
    public async Task PlayerRecords_SortByRating_Descending()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(first: 8, order: { rating: DESC }) {
                            nodes {
                                playerId
                                rating
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
        var ratings = nodes
            .EnumerateArray()
            .Select(n => n.GetProperty("rating").GetInt32())
            .ToList();

        ratings.Should().BeInDescendingOrder();
    }

    [Test]
    public async Task PlayerRecords_FilterAndSort()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(
                            first: 10
                            where: { wins: { gte: 80 } }
                            order: { wins: DESC }
                        ) {
                            nodes {
                                playerId
                                wins
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
        nodes.GetArrayLength().Should().BeGreaterThan(0);

        var wins = nodes.EnumerateArray().Select(n => n.GetProperty("wins").GetInt32()).ToList();
        wins.Should().AllSatisfy(w => w.Should().BeGreaterThanOrEqualTo(80));
        wins.Should().BeInDescendingOrder();
    }
}
