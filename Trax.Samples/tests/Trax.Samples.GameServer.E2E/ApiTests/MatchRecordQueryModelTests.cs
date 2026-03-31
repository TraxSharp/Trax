using System.Text.Json;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class MatchRecordQueryModelTests : ApiTestFixture
{
    [Test]
    public async Task MatchRecords_ReturnsPaginatedData()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    matches {
                        matchRecords(first: 3) {
                            nodes {
                                matchId
                                region
                                winnerScore
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

        var nodes = result.GetData("discover", "matches", "matchRecords", "nodes");
        nodes.GetArrayLength().Should().Be(3);

        var pageInfo = result.GetData("discover", "matches", "matchRecords", "pageInfo");
        pageInfo
            .GetProperty("hasNextPage")
            .GetBoolean()
            .Should()
            .BeTrue("6 seeded matches, first 3 requested");
    }

    [Test]
    public async Task MatchRecords_FilterByRegion()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    matches {
                        matchRecords(first: 10, where: { region: { eq: "na" } }) {
                            nodes {
                                matchId
                                region
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

        var nodes = result.GetData("discover", "matches", "matchRecords", "nodes");
        nodes.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var node in nodes.EnumerateArray())
        {
            node.GetProperty("region").GetString().Should().Be("na");
        }
    }

    [Test]
    public async Task MatchRecords_FilterByWinnerId()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    matches {
                        matchRecords(first: 10, where: { winnerId: { eq: "player-1" } }) {
                            nodes {
                                matchId
                                winnerId
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

        var nodes = result.GetData("discover", "matches", "matchRecords", "nodes");
        nodes.GetArrayLength().Should().BeGreaterThan(0);

        foreach (var node in nodes.EnumerateArray())
        {
            node.GetProperty("winnerId").GetString().Should().Be("player-1");
        }
    }

    [Test]
    public async Task MatchRecords_CursorPagination()
    {
        // Page 1
        var page1 = await GraphQL.SendAsync(
            """
            {
                discover {
                    matches {
                        matchRecords(first: 2) {
                            nodes {
                                matchId
                            }
                            pageInfo {
                                hasNextPage
                                endCursor
                            }
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        page1
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {page1.FirstErrorMessage} (HTTP {page1.StatusCode})");

        var page1Nodes = page1.GetData("discover", "matches", "matchRecords", "nodes");
        page1Nodes.GetArrayLength().Should().Be(2);

        var endCursor = page1
            .GetData("discover", "matches", "matchRecords", "pageInfo")
            .GetProperty("endCursor")
            .GetString();

        endCursor.Should().NotBeNullOrEmpty();

        // Page 2
        var page2 = await GraphQL.SendAsync(
            $$"""
            {
                discover {
                    matches {
                        matchRecords(first: 2, after: "{{endCursor}}") {
                            nodes {
                                matchId
                            }
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        page2
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {page2.FirstErrorMessage} (HTTP {page2.StatusCode})");

        var page2Nodes = page2.GetData("discover", "matches", "matchRecords", "nodes");
        page2Nodes.GetArrayLength().Should().BeGreaterThan(0);

        // Verify no overlap
        var page1Ids = page1Nodes
            .EnumerateArray()
            .Select(n => n.GetProperty("matchId").GetString())
            .ToHashSet();

        foreach (var node in page2Nodes.EnumerateArray())
        {
            var matchId = node.GetProperty("matchId").GetString();
            page1Ids.Should().NotContain(matchId, "page 2 should not overlap with page 1");
        }
    }
}
