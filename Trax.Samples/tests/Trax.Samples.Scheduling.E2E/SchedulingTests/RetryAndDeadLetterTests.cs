using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Scheduling.E2E.Utilities;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// A failing manifest is retried with backoff <c>MaxRetries</c> times and then dead-lettered,
/// and the scheduler stops running it.
/// </summary>
public class RetryAndDeadLetterTests : SchedulingTestFixture
{
    [Test]
    public async Task Failing_manifest_retries_MaxRetries_times_with_backoff_then_dead_letters()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);
        var failures = outage.Failures;

        // DefaultRetryDelay 2 s, RetryBackoffMultiplier 2.0: the first retry waits at least 2 s
        // after the failure, the second at least 4 s.
        (failures[1].StartTime - failures[0].EndTime!.Value)
            .Should()
            .BeGreaterThan(TimeSpan.FromSeconds(1.9));
        (failures[2].StartTime - failures[1].EndTime!.Value)
            .Should()
            .BeGreaterThan(TimeSpan.FromSeconds(3.9));

        outage.DeadLetter.Reason.Should().Contain("3").And.Contain("2");
        outage.DeadLetter.RetryCountAtDeadLetter.Should().Be(3);

        // No fourth attempt: a dead-lettered manifest is skipped until it is resolved. Were it
        // retried, the next attempt would start after an 8 s backoff (2 s doubled twice) plus up
        // to a 1 s manager poll, a 1 s dispatcher poll and a 250 ms worker poll, so the window has
        // to outlast that: 12 s.
        var ranAgain = await Polling.WaitUntilAsync(
            async () => (await Db.Runs(outage.ManifestId, failures[2].Id)).Count > 0,
            TimeSpan.FromSeconds(12),
            TimeSpan.FromMilliseconds(250)
        );
        ranAgain.Should().BeFalse("the scheduler does not run a dead-lettered manifest");
    }

    [Test]
    public async Task Each_failed_run_records_where_and_why_it_failed()
    {
        var outage = await SupplierOutage.RunToDeadLetter(Db, SupplierFeed, Scheduler);

        outage
            .Failures.Should()
            .AllSatisfy(run =>
            {
                run.FailureJunction.Should().Be("DownloadFeedJunction");
                run.FailureException.Should().Be("HttpRequestException");
                run.FailureReason.Should().Contain("503 Service Unavailable");
                run.StackTrace.Should().Contain("DownloadFeedJunction");
                run.EndTime.Should().NotBeNull();
            });
    }
}
