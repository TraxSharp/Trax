using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// SLA tests for <c>requeueAllDeadLetters</c> and <c>acknowledgeAllDeadLetters</c>, whose scope
/// is every dead letter awaiting intervention.
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
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class DeadLetterBatchStressTests : StressTestSetup
{
    /// <summary>
    /// Budget for acknowledging every awaiting dead letter in the seed (500,000 at the default
    /// profile) in one call: measured at about 7.5 s, with headroom for a slower machine.
    /// </summary>
    private static readonly TimeSpan AcknowledgeAllBudget = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Budget for requeueing every awaiting dead letter in the seed in one call, which also writes
    /// one work queue entry per manifest: measured at about 36 s, with headroom for a slower
    /// machine. It is the slowest operation on the surface, and an operator runs it rarely.
    /// </summary>
    private static readonly TimeSpan RequeueAllBudget = TimeSpan.FromSeconds(55);

    /// <summary>The seed's status for dead letter <c>g</c>.</summary>
    private const string SeededStatus =
        "(ARRAY['awaiting_intervention','awaiting_intervention','retried','acknowledged']"
        + "::trax.dead_letter_status[])[1 + (id % 4)]";

    private static ITraxScheduler Scheduler(IServiceProvider sp) =>
        sp.GetRequiredService<ITraxScheduler>();

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
    public async Task RequeueAllDeadLetters_WholeSeed_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.deadLetters.requeueAllDeadLetters",
            RequeueAllBudget,
            RestoreSeed,
            async (sp, ct) =>
                (await new DeadLetterMutations().RequeueAllDeadLetters(Scheduler(sp), ct))
                    .Count.Should()
                    .Be((int)SeededAwaiting)
        );
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_WholeSeed_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.deadLetters.acknowledgeAllDeadLetters",
            AcknowledgeAllBudget,
            RestoreSeed,
            async (sp, ct) =>
                (
                    await new DeadLetterMutations().AcknowledgeAllDeadLetters(
                        "stress",
                        Scheduler(sp),
                        ct
                    )
                )
                    .Count.Should()
                    .Be((int)SeededAwaiting)
        );
    }
}
