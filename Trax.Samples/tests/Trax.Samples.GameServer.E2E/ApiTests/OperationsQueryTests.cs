using System.Text.Json;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class OperationsQueryTests : ApiTestFixture
{
    [Test]
    public async Task GetHealth_ReturnsStatus()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                operations {
                    health {
                        status
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var health = result.GetData("operations", "health");
        health.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Test]
    public async Task GetTrains_ReturnsRegisteredTrains()
    {
        var result = await GraphQL.SendAsync(
            """
            {
                operations {
                    trains {
                        serviceTypeName
                        inputTypeName
                        isQuery
                        isMutation
                        isBroadcastEnabled
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var trains = result.GetData("operations", "trains");
        trains.GetArrayLength().Should().BeGreaterThan(0);

        // LookupPlayer should be a query
        var lookupPlayer = trains
            .EnumerateArray()
            .FirstOrDefault(t =>
                t.GetProperty("serviceTypeName").GetString()?.Contains("LookupPlayer") == true
            );

        lookupPlayer
            .ValueKind.Should()
            .NotBe(JsonValueKind.Undefined, "LookupPlayer should be registered");
        lookupPlayer.GetProperty("isQuery").GetBoolean().Should().BeTrue();

        // ProcessMatchResult should be a broadcast-enabled mutation
        var processMatch = trains
            .EnumerateArray()
            .FirstOrDefault(t =>
                t.GetProperty("serviceTypeName").GetString()?.Contains("ProcessMatchResult") == true
            );

        processMatch
            .ValueKind.Should()
            .NotBe(JsonValueKind.Undefined, "ProcessMatchResult should be registered");
        processMatch.GetProperty("isMutation").GetBoolean().Should().BeTrue();
        processMatch.GetProperty("isBroadcastEnabled").GetBoolean().Should().BeTrue();
    }

    // GetManifests and GetManifest tests are in SchedulerTests/ManifestOperationsQueryTests.cs
    // because they require manifests to be seeded first (SharedSchedulerSetup).

    [Test]
    public async Task GetExecutions_AfterTrainRun()
    {
        // Run a train to create metadata.
        var runResult = await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        lookupPlayer(input: { playerId: "player-1" }) {
                            playerId
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        runResult.HasErrors.Should().BeFalse();

        // Wait for metadata to persist.
        await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "LookupPlayer",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        // Query executions.
        var result = await GraphQL.SendAsync(
            """
            {
                operations {
                    executions(take: 5) {
                        items {
                            id
                            name
                            trainState
                        }
                        totalCount
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var executions = result.GetData("operations", "executions");
        executions.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);

        var items = executions.GetProperty("items");
        var lookupExecution = items
            .EnumerateArray()
            .Any(e => e.GetProperty("name").GetString()?.Contains("LookupPlayer") == true);

        lookupExecution.Should().BeTrue("LookupPlayer execution should appear in the list");
    }

    [Test]
    public async Task GetExecution_ById_ReturnsSingle()
    {
        // Run a train.
        await GraphQL.SendAsync(
            """
            {
                discover {
                    players {
                        lookupPlayer(input: { playerId: "player-2" }) {
                            playerId
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "LookupPlayer",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        // Query by ID.
        var result = await GraphQL.SendAsync(
            $$"""
            {
                operations {
                    execution(id: {{metadata.Id}}) {
                        id
                        name
                        trainState
                        startTime
                    }
                }
            }
            """
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var execution = result.GetData("operations", "execution");
        execution.GetProperty("id").GetInt64().Should().Be(metadata.Id);
        execution.GetProperty("name").GetString().Should().Contain("LookupPlayer");
        execution.GetProperty("trainState").GetString().Should().Be("COMPLETED");
    }
}
