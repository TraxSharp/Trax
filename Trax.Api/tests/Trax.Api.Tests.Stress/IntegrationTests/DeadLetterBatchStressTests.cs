using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Tests.Stress.Fixtures;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// SLA tests for <c>requeueAllDeadLetters</c> and <c>acknowledgeAllDeadLetters</c>, whose scope
/// is every dead letter awaiting intervention, run through the schema's request executor so the
/// operations gate, the error filter, serialization and HotChocolate's execution timeout are all
/// under the load. The requeue-all case enforces
/// <c>docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md</c>: the mutation
/// answers within a second and the fold finishes in the background.
/// </summary>
/// <remarks>
/// They run over the whole seed: every dead letter the seed leaves awaiting intervention, half of
/// its <c>DeadLetter</c> rows, spread over every manifest. A requeue-all folds the dead letters
/// that share a manifest into one work queue entry. Each run starts from the seed's statuses and
/// with no dead-letter work queue entries, and the fixture puts them back when it finishes, so
/// the read and single-row suites see the seed whichever order NUnit runs the fixtures in.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Property("adr", "docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class DeadLetterBatchStressTests : StressTestSetup
{
    private const string Adr =
        "docs/adr/0036-requeue-all-runs-in-the-background-and-returns-a-handle.md";

    protected override void ConfigureServices(IServiceCollection services) =>
        AddOperationsGraphQL(services);

    /// <summary>
    /// Budget for acknowledging every awaiting dead letter in the seed (500,000 at the default
    /// profile) in one call: measured at about 7.5 s, with headroom for a slower machine.
    /// </summary>
    private static readonly TimeSpan AcknowledgeAllBudget = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Budget for the background fold that requeues every awaiting dead letter in the seed, which
    /// also writes one work queue entry per manifest: measured at about 36 s, with headroom for a
    /// slower machine. It is the slowest operation on the surface, and an operator runs it rarely.
    /// The request that starts it answers within <see cref="RequeueAllAnswerBudget"/>.
    /// </summary>
    private static readonly TimeSpan RequeueAllBudget = TimeSpan.FromSeconds(55);

    /// <summary>
    /// Budget for the request that starts a requeue-all. It counts the dead letters awaiting
    /// intervention (500,000 in the seed) for the handle and starts the job: measured at
    /// 120-380 ms, the slower right after the restore has rewritten half the table. A second is
    /// well inside HotChocolate's 30 s execution timeout, which is what it must never approach.
    /// </summary>
    private static readonly TimeSpan RequeueAllAnswerBudget = TimeSpan.FromSeconds(1);

    /// <summary>The seed's status for dead letter <c>g</c>.</summary>
    private const string SeededStatus =
        "(ARRAY['awaiting_intervention','awaiting_intervention','retried','acknowledged']"
        + "::trax.dead_letter_status[])[1 + (id % 4)]";

    /// <summary>Puts every dead letter back to its seeded status, with no work queue entries.</summary>
    private static Task RestoreSeed() =>
        ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE dead_letter_id IS NOT NULL; "
                + $"UPDATE trax.dead_letter SET status = {SeededStatus}, resolved_at = NULL, "
                + "resolution_note = NULL, retry_metadata_id = NULL "
                + $"WHERE status IS DISTINCT FROM {SeededStatus} OR resolved_at IS NOT NULL"
        );

    [OneTimeTearDown]
    public async Task RestoreSeedAfterwards() => await RestoreSeed();

    /// <summary>The seed leaves dead letters 0 and 1 of every 4 awaiting intervention.</summary>
    private long SeededAwaiting => Profile.DeadLetter / 4 * 2 + Math.Min(Profile.DeadLetter % 4, 1);

    [Test]
    public async Task RequeueAllDeadLetters_WholeSeed_AnswersAtOnceAndFinishesWithinBudget()
    {
        var jobs = Services.GetRequiredService<DeadLetterRequeueJobs>();

        await MeasureWriteAsync(
            "operations.deadLetters.requeueAllDeadLetters (whole fold)",
            RequeueAllBudget,
            RestoreSeed,
            async (_, ct) =>
            {
                var answer = Stopwatch.StartNew();
                var job = await OperationsFieldAsync(
                    "mutation { operations { deadLetters { requeueAllDeadLetters "
                        + "{ id status awaitingAtStart } } } }",
                    ct,
                    "deadLetters"
                );
                answer.Stop();
                TestContext.Out.WriteLine(
                    $"requeueAllDeadLetters answered in {answer.Elapsed.TotalMilliseconds:F0}ms"
                );

                answer
                    .Elapsed.Should()
                    .BeLessThan(
                        RequeueAllAnswerBudget,
                        $"the mutation returns a handle without waiting for the fold ({Adr})"
                    );
                job.GetProperty("status").GetString().Should().Be("RUNNING");
                job.GetProperty("awaitingAtStart").GetInt32().Should().Be((int)SeededAwaiting);

                await jobs.Current;

                var done = await OperationsFieldAsync(
                    "{ operations { deadLetters { requeueAllJob(id: \""
                        + job.GetProperty("id").GetString()
                        + "\") { status count message } } } }",
                    ct,
                    "deadLetters"
                );
                done.GetProperty("status")
                    .GetString()
                    .Should()
                    .Be("SUCCEEDED", done.GetProperty("message").GetString());
                done.GetProperty("count").GetInt32().Should().Be((int)SeededAwaiting);
            }
        );
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_WholeSeed_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.deadLetters.acknowledgeAllDeadLetters",
            AcknowledgeAllBudget,
            RestoreSeed,
            async (_, ct) =>
                (
                    await OperationsFieldAsync(
                        "mutation { operations { deadLetters { acknowledgeAllDeadLetters"
                            + "(note: \"stress\") { count } } } }",
                        ct,
                        "deadLetters"
                    )
                )
                    .GetProperty("count")
                    .GetInt32()
                    .Should()
                    .Be((int)SeededAwaiting)
        );
    }
}
