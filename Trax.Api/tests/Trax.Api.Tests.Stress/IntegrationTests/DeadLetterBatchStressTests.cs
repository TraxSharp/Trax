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
/// <para>
/// These run against a bounded set, not the seed's 500,000 awaiting rows. The pinned
/// Trax.Scheduler resolves them one tracked entity at a time, and a requeue-all that meets two
/// awaiting dead letters for one manifest fails on the one-queued-entry-per-manifest index. So
/// for the fixture's lifetime only the dead letters with ids up to <c>Manifests</c> stay awaiting
/// (half of them, one per manifest), and the rest of the table stays at full size around them,
/// resolved. Once a Scheduler release makes both writes set-based and skips duplicate manifests,
/// the staging goes and these run over the whole seed.
/// </para>
/// <para>
/// The fixture restores the seed's statuses when it finishes, so the read and single-row suites
/// see the seed whichever order NUnit runs the fixtures in.
/// </para>
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class DeadLetterBatchStressTests : StressTestSetup
{
    /// <summary>
    /// A batch over the bounded set: about <c>Manifests / 2</c> rows, each a tracked update, and
    /// for a requeue a work queue insert too.
    /// </summary>
    private static readonly TimeSpan BatchBudget = TimeSpan.FromSeconds(3);

    /// <summary>The seed's status for dead letter <c>g</c>.</summary>
    private const string SeededStatus =
        "(ARRAY['awaiting_intervention','awaiting_intervention','retried','acknowledged']"
        + "::trax.dead_letter_status[])[1 + (id % 4)]";

    private static ITraxScheduler Scheduler(IServiceProvider sp) =>
        sp.GetRequiredService<ITraxScheduler>();

    [OneTimeSetUp]
    public async Task StageBoundedSet() =>
        await ExecSqlAsync(
            "UPDATE trax.dead_letter SET status = 'retried' "
                + $"WHERE status = 'awaiting_intervention' AND id > {Profile.Manifests}"
        );

    [OneTimeTearDown]
    public async Task RestoreSeed() =>
        await ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE dead_letter_id IS NOT NULL; "
                + $"UPDATE trax.dead_letter SET status = {SeededStatus}, resolved_at = NULL, "
                + "resolution_note = NULL, retry_metadata_id = NULL "
                + $"WHERE status IS DISTINCT FROM {SeededStatus} OR resolved_at IS NOT NULL"
        );

    /// <summary>Puts the bounded set back to awaiting, with no work queue entries of its own.</summary>
    private Task ResetBoundedSet() =>
        ExecSqlAsync(
            "DELETE FROM trax.work_queue WHERE dead_letter_id IS NOT NULL; "
                + $"UPDATE trax.dead_letter SET status = {SeededStatus}, resolved_at = NULL, "
                + "resolution_note = NULL, retry_metadata_id = NULL "
                + $"WHERE id <= {Profile.Manifests}"
        );

    private long BoundedAwaiting => (Profile.Manifests / 4) * 2;

    [Test]
    public async Task RequeueAllDeadLetters_BoundedSet_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.deadLetters.requeueAllDeadLetters",
            BatchBudget,
            ResetBoundedSet,
            async (sp, ct) =>
                (await new DeadLetterMutations().RequeueAllDeadLetters(Scheduler(sp), ct))
                    .Count.Should()
                    .BeCloseTo((int)BoundedAwaiting, 2)
        );
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_BoundedSet_WithinBudget()
    {
        await MeasureWriteAsync(
            "operations.deadLetters.acknowledgeAllDeadLetters",
            BatchBudget,
            ResetBoundedSet,
            async (sp, ct) =>
                (
                    await new DeadLetterMutations().AcknowledgeAllDeadLetters(
                        "stress",
                        Scheduler(sp),
                        ct
                    )
                )
                    .Count.Should()
                    .BeCloseTo((int)BoundedAwaiting, 2)
        );
    }
}
