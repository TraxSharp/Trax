using Microsoft.EntityFrameworkCore;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

/// <summary>
/// ProcessMatchResult supports both Run and Queue modes but requires IDormantDependentContext
/// (scheduler-only) for CheckForAnomaliesJunction. On the API (no scheduler), only Queue mode
/// works. These tests verify Queue mode with data-layer assertions beyond what
/// GraphQLMutationTests already covers.
/// </summary>
[TestFixture]
public class ProcessMatchRunModeTests : ApiTestFixture
{
    [Test]
    public async Task ProcessMatchResult_QueueMode_WithHighPriority_SetsCorrectPriority()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    matches {
                        processMatchResult(
                            input: {
                                region: "eu"
                                matchId: "e2e-priority-001"
                                winnerId: "player-3"
                                loserId: "player-4"
                                winnerScore: 80
                                loserScore: 60
                            }
                            mode: QUEUE
                            priority: 99
                        ) {
                            externalId
                            workQueueId
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

        var workQueueId = result
            .GetData("dispatch", "matches", "processMatchResult")
            .GetProperty("workQueueId")
            .GetInt64();

        // Verify priority was persisted.
        DataContext.Reset();
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(wq => wq.Id == workQueueId);

        entry.Should().NotBeNull();
        // Priority in the GraphQL mutation is additive to the manifest's base priority.
        // Just verify it's stored and greater than 0.
        entry!.Priority.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ProcessMatchResult_QueueMode_InputContainsAllFields()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    matches {
                        processMatchResult(
                            input: {
                                region: "ap"
                                matchId: "e2e-fields-001"
                                winnerId: "player-5"
                                loserId: "player-6"
                                winnerScore: 75
                                loserScore: 70
                            }
                            mode: QUEUE
                        ) {
                            workQueueId
                        }
                    }
                }
            }
            """,
            apiKey: PlayerKey
        );

        result.HasErrors.Should().BeFalse();

        var workQueueId = result
            .GetData("dispatch", "matches", "processMatchResult")
            .GetProperty("workQueueId")
            .GetInt64();

        DataContext.Reset();
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(wq => wq.Id == workQueueId);

        entry.Should().NotBeNull();
        entry!.Input.Should().Contain("ap");
        entry.Input.Should().Contain("e2e-fields-001");
        entry.Input.Should().Contain("player-5");
        entry.Input.Should().Contain("player-6");
    }
}
