using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.Services.BulkOperations;
using Trax.Api.Tests.Stress.Fixtures;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// SLA tests for <c>startBulkOperation</c>, which has no cap on how many rows it matches, run
/// through the schema's request executor. A cancel over every run in progress and a requeue over
/// every dead letter awaiting intervention in the seed must answer quickly, finish, keep every
/// batch short, and leave the list reads the console makes meanwhile within their budget. Enforces
/// <c>docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md</c> as
/// evidence; it is an explicit stress fixture, so CI does not run it.
/// </summary>
/// <remarks>
/// Each test starts from the seed and puts it back when it finishes, so the read suites see the
/// seed whichever order NUnit runs the fixtures in.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Property("adr", "docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class BulkOperationStressTests : StressTestSetup
{
    private const string Adr =
        "docs/adr/0045-a-bulk-operation-applies-to-a-confirmed-snapshot-of-a-filter.md";

    protected override void ConfigureServices(IServiceCollection services) =>
        AddOperationsGraphQL(services);

    /// <summary>
    /// Budget for the request that starts an operation: it reads every matching id, a page of
    /// 10,000 at a time, and must stay well inside HotChocolate's 30 s execution timeout.
    /// </summary>
    private static readonly TimeSpan StartBudget = TimeSpan.FromSeconds(10);

    /// <summary>Budget for the whole operation, from the answer to its last batch.</summary>
    private static readonly TimeSpan FinishBudget = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Budget for one batch: one shared operation over at most 1,000 ids and the read-back of its
    /// rows. A batch is the longest anything holds a transaction.
    /// </summary>
    private static readonly TimeSpan BatchBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Budget for a list read while an operation runs: the runs list's first page at the largest
    /// size the operations surface serves, 500 rows, read under the write load on the same table.
    /// </summary>
    private static readonly TimeSpan ReadMeanwhileBudget = ListBudget * 2;

    /// <summary>The seed's status for each dead letter, as DeadLetterBatchStressTests restores it.</summary>
    private const string SeededStatus =
        "(ARRAY['awaiting_intervention','awaiting_intervention','retried','acknowledged']"
        + "::trax.dead_letter_status[])[1 + (id % 4)]";

    private static Task RestoreDeadLetters() =>
        ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE dead_letter_id IS NOT NULL; "
                + $"UPDATE trax.dead_letter SET status = {SeededStatus}, resolved_at = NULL, "
                + "resolution_note = NULL, retry_metadata_id = NULL "
                + $"WHERE status IS DISTINCT FROM {SeededStatus} OR resolved_at IS NOT NULL"
        );

    private static Task RestoreRuns() =>
        ExecSqlAsync("UPDATE trax.metadata SET cancel_requested = false WHERE cancel_requested");

    [OneTimeTearDown]
    public async Task RestoreSeedAfterwards()
    {
        await RestoreRuns();
        await RestoreDeadLetters();
    }

    [Test]
    public async Task Cancel_EveryRunInProgress_FinishesWithShortBatchesWhileReadsAreServed()
    {
        await RestoreRuns();
        var matching = await ScalarAsync<int>(
            "SELECT count(*) FROM trax.metadata WHERE train_state = 'in_progress'"
        );

        await RunAsync(
            "CANCEL",
            "{ executions: { trainState: IN_PROGRESS } }",
            matching,
            expectedDone: matching
        );
        await RestoreRuns();
    }

    [Test]
    public async Task Requeue_EveryDeadLetterAwaiting_FinishesWithShortBatchesWhileReadsAreServed()
    {
        await RestoreDeadLetters();
        var matching = await ScalarAsync<int>(
            "SELECT count(*) FROM trax.dead_letter WHERE status = 'awaiting_intervention'"
        );

        await RunAsync(
            "REQUEUE",
            "{ deadLetters: { status: AWAITING_INTERVENTION } }",
            matching,
            expectedDone: matching
        );
        await RestoreDeadLetters();
    }

    private async Task RunAsync(string kind, string filter, int matching, int expectedDone)
    {
        var jobs = Services.GetRequiredService<BulkOperationJobs>();

        var answer = Stopwatch.StartNew();
        var started = await OperationsFieldAsync(
            $"mutation {{ operations {{ startBulkOperation(kind: {kind}, filter: {filter}, "
                + $"expectedCount: {matching}) {{ success message matched operation {{ id total }} }} }} }}"
        );
        answer.Stop();
        TestContext.Out.WriteLine(
            $"startBulkOperation({kind}) over {matching:N0} answered in "
                + $"{answer.Elapsed.TotalMilliseconds:F0}ms"
        );

        started
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(started.GetProperty("message").GetString());
        answer
            .Elapsed.Should()
            .BeLessThan(StartBudget, $"the snapshot is read in short pages ({Adr})");
        var id = Guid.Parse(started.GetProperty("operation").GetProperty("id").GetString()!);

        var finish = Stopwatch.StartNew();
        var reads = new List<TimeSpan>();
        while (jobs.Get(id)?.Status == BulkOperationStatus.Running)
        {
            var read = Stopwatch.StartNew();
            await OperationsFieldAsync(
                "{ operations { executions(take: 500) { totalCount items { id } } } }"
            );
            read.Stop();
            reads.Add(read.Elapsed);
            finish
                .Elapsed.Should()
                .BeLessThan(FinishBudget, $"the operation over every match finishes ({Adr})");
        }
        finish.Stop();

        var operation = jobs.Get(id)!;
        var run = jobs.Run(id)!;
        TestContext.Out.WriteLine(
            $"{kind}: {operation.Done:N0} done, {operation.Failed:N0} failed in "
                + $"{finish.Elapsed.TotalSeconds:F1}s over {run.Batches} batches; longest batch "
                + $"{run.LongestBatch.TotalMilliseconds:F0}ms; {reads.Count} reads meanwhile, "
                + $"slowest {(reads.Count == 0 ? 0 : reads.Max().TotalMilliseconds):F0}ms"
        );

        operation.Status.Should().Be(BulkOperationStatus.Finished, operation.Message);
        operation.Total.Should().Be(matching);
        operation.Done.Should().Be(expectedDone, string.Join("; ", operation.FailureReasons));
        run.LongestBatch.Should()
            .BeLessThan(BatchBudget, $"no transaction outlives one short batch ({Adr})");
        reads.Should().NotBeEmpty("the reads are measured while the operation runs");
        reads
            .Max()
            .Should()
            .BeLessThan(
                ReadMeanwhileBudget,
                $"a 500-row list read is served between batches ({Adr})"
            );
    }
}
