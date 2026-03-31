using Microsoft.EntityFrameworkCore;
using Trax.Samples.GameServer.E2E.Fixtures;

namespace Trax.Samples.GameServer.E2E.ApiTests;

[TestFixture]
public class RecalculateLeaderboardMutationTests : ApiTestFixture
{
    [Test]
    public async Task RecalculateLeaderboard_QueueMode_EnqueuesWork()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    leaderboard {
                        recalculateLeaderboard(
                            input: { region: "global" }
                            priority: 5
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

        var dispatch = result.GetData("dispatch", "leaderboard", "recalculateLeaderboard");
        dispatch.GetProperty("externalId").GetString().Should().NotBeNullOrEmpty();

        var workQueueId = dispatch.GetProperty("workQueueId").GetInt64();
        workQueueId.Should().BeGreaterThan(0);

        // Verify work queue entry in DB.
        DataContext.Reset();
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(wq => wq.Id == workQueueId);

        entry.Should().NotBeNull("work queue entry should exist in the database");
        entry!.TrainName.Should().Contain("RecalculateLeaderboard");
    }

    [Test]
    public async Task RecalculateLeaderboard_QueueMode_VerifiesInput()
    {
        var result = await GraphQL.SendAsync(
            """
            mutation {
                dispatch {
                    leaderboard {
                        recalculateLeaderboard(
                            input: { region: "eu-west" }
                        ) {
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
            .GetData("dispatch", "leaderboard", "recalculateLeaderboard")
            .GetProperty("workQueueId")
            .GetInt64();

        DataContext.Reset();
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .FirstOrDefaultAsync(wq => wq.Id == workQueueId);

        entry.Should().NotBeNull();
        entry!.Input.Should().Contain("eu-west");
    }
}
