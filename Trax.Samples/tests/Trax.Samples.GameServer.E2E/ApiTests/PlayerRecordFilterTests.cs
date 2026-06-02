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

    [Test]
    public async Task PlayerRecords_FilterByDisplayName_IContains_IsCaseInsensitive()
    {
        // Seed data has "ShadowBlade" (capital B). The lowercase term "blade" only
        // matches it case-insensitively, so icontains must translate to lower() SQL.
        var ci = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(where: { displayName: { icontains: "blade" } }) {
                            nodes { displayName }
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        ci.HasErrors.Should()
            .BeFalse($"GraphQL error: {ci.FirstErrorMessage} (HTTP {ci.StatusCode})");

        var ciNames = ci.GetData("discover", "players", "playerRecords", "nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("displayName").GetString()!)
            .ToList();

        ciNames.Should().Contain("ShadowBlade");
        ciNames.Should().OnlyContain(n => n.ToLower().Contains("blade"));

        // The stock case-sensitive `contains` with the same lowercase term must NOT
        // match "ShadowBlade", which is exactly what makes icontains worth having.
        var cs = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(where: { displayName: { contains: "blade" } }) {
                            nodes { displayName }
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        cs.HasErrors.Should()
            .BeFalse($"GraphQL error: {cs.FirstErrorMessage} (HTTP {cs.StatusCode})");

        var csNames = cs.GetData("discover", "players", "playerRecords", "nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("displayName").GetString()!)
            .ToList();

        csNames.Should().NotContain("ShadowBlade");
    }

    [Test]
    public async Task PlayerRecords_FilterByDisplayName_IEq_IsCaseInsensitive()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        playerRecords(where: { displayName: { ieq: "shadowblade" } }) {
                            nodes { displayName }
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

        var names = result
            .GetData("discover", "players", "playerRecords", "nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("displayName").GetString()!)
            .ToList();

        // ieq matches the exact value ignoring case, so the lowercase term resolves to
        // the seeded "ShadowBlade" and nothing else.
        names.Should().ContainSingle().Which.Should().Be("ShadowBlade");
    }
}
