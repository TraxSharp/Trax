using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class GraphQLMutationTests : ApiTestFixture
{
    [Test]
    public async Task BanPlayer_RunMutation_ExecutesInline()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    players {
                        banPlayer(
                            input: { playerId: "player-8", reason: "E2E test ban" }
                        ) {
                            externalId
                        }
                    }
                }
            }
            """,
            apiKey: AdminKey
        );

        result
            .HasErrors.Should()
            .BeFalse($"GraphQL error: {result.FirstErrorMessage} (HTTP {result.StatusCode})");

        var externalId = result
            .GetData("dispatch", "players", "banPlayer")
            .GetProperty("externalId")
            .GetString();
        externalId.Should().NotBeNullOrEmpty();

        // Verify the train actually executed and metadata was persisted.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "BanPlayer",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
        metadata.ExternalId.Should().Be(externalId);
    }

    [Test]
    public async Task ProcessMatchResult_QueueMutation_EnqueuesWork()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    matches {
                        processMatchResult(
                            input: {
                                region: "na"
                                matchId: "e2e-queue-001"
                                winnerId: "player-1"
                                loserId: "player-2"
                                winnerScore: 80
                                loserScore: 60
                            }
                            mode: QUEUE
                            priority: 10
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

        var dispatch = result.GetData("dispatch", "matches", "processMatchResult");
        var externalId = dispatch.GetProperty("externalId").GetString();
        var workQueueId = dispatch.GetProperty("workQueueId").GetInt64();

        externalId.Should().NotBeNullOrEmpty();
        workQueueId.Should().BeGreaterThan(0);

        // Verify the work queue entry actually exists in the database.
        DataContext.Reset();
        var workQueueEntry = await DataContext
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(wq => wq.Id == workQueueId);

        workQueueEntry.Should().NotBeNull("the work queue entry should exist in the database");
        workQueueEntry!.TrainName.Should().Contain("ProcessMatchResult");
        workQueueEntry.Input.Should().Contain("e2e-queue-001");
    }
}
