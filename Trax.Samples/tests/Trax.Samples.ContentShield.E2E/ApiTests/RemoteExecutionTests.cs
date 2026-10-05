using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.ContentShield.E2E.Fixtures;
using Trax.Samples.ContentShield.E2E.Utilities;

namespace Trax.Samples.ContentShield.E2E.ApiTests;

/// <summary>
/// The API dispatches; the runner executes. A queued mutation is POSTed to the runner's
/// <c>/trax/execute</c>; a run mutation and a query, both synchronous runs, to <c>/trax/run</c>.
/// </summary>
[TestFixture]
public class RemoteExecutionTests : ApiTestFixture
{
    [Test]
    public async Task Query_is_run_on_the_runner()
    {
        var before = Runner.Requests.Runs;

        var result = await GetGraphQLClient()
            .SendAsync(
                """
                {
                    discover {
                        moderation {
                            lookupModerationResult(input: { contentId: "test-001" }) {
                                contentId
                                moderationStatus
                            }
                        }
                    }
                }
                """
            );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        result
            .GetData("discover", "moderation", "lookupModerationResult")
            .GetProperty("contentId")
            .GetString()
            .Should()
            .Be("test-001");
        Runner
            .Requests.Runs.Should()
            .Be(before + 1, "UseRemoteRun sends every synchronous run, queries included");
    }

    [Test]
    public async Task Queued_review_is_executed_by_the_runner()
    {
        var before = Runner.Requests.Executes;

        var result = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        moderation {
                            reviewContent(
                                input: { contentId: "e2e-1", contentType: "video", contentBody: "suspicious video" }
                            ) {
                                externalId
                                workQueueId
                            }
                        }
                    }
                }
                """
            );
        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "ReviewContent",
            TrainState.Completed
        );
        metadata.Should().NotBeNull();
        Runner.Requests.Executes.Should().BeGreaterThan(before);
    }

    [Test]
    public async Task Run_mutation_waits_for_the_runner_and_returns_its_output()
    {
        var before = Runner.Requests.Runs;

        var result = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        reports {
                            generateModerationReport(input: { reportPeriod: "Daily" }) {
                                output { totalReviewed topViolationTypes }
                            }
                        }
                    }
                }
                """,
                apiKey: ModeratorKey
            );

        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);
        result
            .GetData("dispatch", "reports", "generateModerationReport", "output")
            .GetProperty("totalReviewed")
            .GetInt32()
            .Should()
            .BeGreaterThan(0);
        Runner.Requests.Runs.Should().Be(before + 1);
    }

    [Test]
    public async Task A_routed_train_never_touches_the_background_job_table()
    {
        // A local worker deletes its background_job row once the run ends, so counting rows
        // afterwards proves nothing. The id sequence remembers an insert that was deleted since.
        var jobIdsBefore = await LastBackgroundJobIdAsync();
        var executesBefore = Runner.Requests.Executes;

        var result = await GetGraphQLClient()
            .SendAsync(
                """
                mutation {
                    dispatch {
                        moderation {
                            reviewContent(
                                input: { contentId: "e2e-2", contentType: "text", contentBody: "hi" }
                            ) { externalId }
                        }
                    }
                }
                """
            );
        result.HasErrors.Should().BeFalse(result.FirstErrorMessage);

        await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "ReviewContent",
            TrainState.Completed
        );

        // UseRemoteWorkers routes these trains to HTTP. The API still registers the local
        // worker (an unrouted train would fall back to it), but it never gets a job to claim.
        (await LastBackgroundJobIdAsync())
            .Should()
            .Be(jobIdsBefore, "no background_job row was ever inserted for the routed train");
        Runner
            .Requests.Executes.Should()
            .Be(executesBefore + 1, "the runner executed the queued review");
    }

    /// <summary>The last id the background_job table handed out, or 0 before its first insert.</summary>
    private async Task<long> LastBackgroundJobIdAsync() =>
        await ((DbContext)DataContext)
            .Database.SqlQueryRaw<long>(
                """
                SELECT COALESCE(pg_sequence_last_value(pg_get_serial_sequence('trax.background_job', 'id')::regclass), 0) AS "Value"
                """
            )
            .SingleAsync();
}
