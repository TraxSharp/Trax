using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.SnapshotTransition;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// One state-machine instance's history as the operations surface reads it for the console's instance page and the
/// API's <c>machineInstance { transitions }</c>: newest first, paged by revision, with when the previous transition was
/// written and what each one started, and never a context, an owner key or a run's token.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class OperationsServiceMachineInstanceTransitionsTests : TestSetup
{
    private IOperationsService _operations = null!;
    private string _machine = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
        _machine = $"ops-history-{Guid.NewGuid():N}";
    }

    [TearDown]
    public async Task DeleteDrafts() =>
        await DataContext
            .SnapshotDrafts.Where(x => x.Machine.StartsWith(_machine))
            .ExecuteDeleteAsync();

    private async Task<SnapshotDraft> Seed(
        SnapshotOwnerKind owner,
        string state,
        string? machine = null,
        string? spawnedBy = null
    )
    {
        var row = new SnapshotDraft
        {
            Id = Guid.NewGuid(),
            OwnerKind = owner,
            UserKey = owner == SnapshotOwnerKind.User ? $"user-{Guid.NewGuid():N}" : null,
            Machine = machine ?? _machine,
            Version = 1,
            State = state,
            Context = """{"secret":"never shown"}""",
            ConcurrencyToken = Guid.NewGuid(),
            UpdatedAt = DateTimeOffset.UtcNow,
            SpawnedBy = spawnedBy,
        };
        DataContext.SnapshotDrafts.Add(row);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return row;
    }

    private async Task History(SnapshotDraft row, params SnapshotTransition[] transitions)
    {
        foreach (var t in transitions)
        {
            t.DraftRowId = row.RowId;
            DataContext.SnapshotTransitions.Add(t);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    private static SnapshotTransition Row(
        long revision,
        SnapshotTransitionKind kind,
        string? trigger,
        string? from,
        string to,
        DateTimeOffset at,
        string? token = null,
        SnapshotTransitionActor actor = SnapshotTransitionActor.Host
    ) =>
        new()
        {
            Revision = revision,
            Kind = kind,
            Trigger = trigger,
            Actor = actor,
            FromState = from,
            ToState = to,
            At = at,
            InvokeToken = token,
        };

    private static MachineInstanceKey KeyOf(SnapshotDraft row) =>
        new(row.Machine, row.OwnerKind, row.Id, row.RowId);

    [Test]
    public async Task The_history_is_newest_first_paged_by_revision_with_the_time_before_each()
    {
        var instance = await Seed(SnapshotOwnerKind.System, "Done");
        var t0 = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await History(
            instance,
            Row(1, SnapshotTransitionKind.Start, null, null, "Draft", t0),
            Row(2, SnapshotTransitionKind.Command, "Submit", "Draft", "Review", t0.AddMinutes(1)),
            Row(
                3,
                SnapshotTransitionKind.Timer,
                "Review.after",
                "Review",
                "Expired",
                t0.AddMinutes(4)
            ),
            Row(5, SnapshotTransitionKind.Command, "Archive", "Expired", "Done", t0.AddMinutes(9))
        );

        var first = await _operations.GetMachineInstanceTransitionsAsync(
            KeyOf(instance),
            beforeRevision: null,
            take: 2,
            CancellationToken.None
        );
        var second = await _operations.GetMachineInstanceTransitionsAsync(
            KeyOf(instance),
            first!.NextCursor,
            take: 2,
            CancellationToken.None
        );

        first.Items.Select(t => t.Revision).Should().Equal(5, 3);
        first.NextCursor.Should().Be(3, "another page follows");
        first.Items[0].PreviousAt.Should().Be(t0.AddMinutes(4), "the transition before it");
        first
            .Items[1]
            .PreviousAt.Should()
            .Be(t0.AddMinutes(1), "the row past the page gives the last one its previous time");
        first.Items[1].Kind.Should().Be(SnapshotTransitionKind.Timer);
        first.Items[1].Trigger.Should().Be("Review.after");
        first.Items[1].FromState.Should().Be("Review");
        first.Items[1].ToState.Should().Be("Expired");
        second!.Items.Select(t => t.Revision).Should().Equal(2, 1);
        second.NextCursor.Should().BeNull("the oldest is on this page");
        second.Items[1].PreviousAt.Should().BeNull("nothing came before the start");
    }

    [Test]
    public async Task Each_transition_names_the_run_or_the_child_it_started_never_the_token()
    {
        var instance = await Seed(SnapshotOwnerKind.System, "Waiting");
        var ran = Guid.NewGuid().ToString("N");
        var queued = Guid.NewGuid().ToString("N");
        var spawned = Guid.NewGuid().ToString("N");
        var child = await Seed(
            SnapshotOwnerKind.System,
            "Packing",
            machine: _machine + "-child",
            spawnedBy: spawned
        );
        var at = DateTimeOffset.UtcNow;
        await History(
            instance,
            Row(1, SnapshotTransitionKind.Start, null, null, "Running", at, ran),
            Row(2, SnapshotTransitionKind.Outcome, "Running.done", "Running", "Queued", at, queued),
            Row(3, SnapshotTransitionKind.Command, "Spawn", "Queued", "Waiting", at, spawned)
        );
        var db = (DbContext)DataContext;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time, failure_class)
            VALUES ({ran}, 'Ops.IHistoryTrain', 'completed', now() - interval '2 minutes', now() - interval '1 minute', 'unclassified')
            """
        );
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO trax.work_queue (external_id, train_name, status, priority, created_at)
            VALUES ({queued}, 'Ops.IHistoryTrain', 'queued', 0, now())
            """
        );

        try
        {
            var page = await _operations.GetMachineInstanceTransitionsAsync(
                KeyOf(instance),
                null,
                25,
                CancellationToken.None
            );
            var byRevision = page!.Items.ToDictionary(t => t.Revision);

            byRevision[1].StartedRun!.ExecutionId.Should().NotBeNull();
            byRevision[1].StartedRun!.TrainState.Should().Be(TrainState.Completed);
            byRevision[2].StartedRun!.ExecutionId.Should().BeNull("the run is still queued");
            byRevision[2].StartedRun!.TrainState.Should().Be(TrainState.Pending);
            byRevision[2].StartedRun!.WorkQueueEntryId.Should().NotBeNull();
            byRevision[3].StartedRun.Should().BeNull();
            byRevision[3]
                .StartedChild.Should()
                .Be(
                    new MachineInstanceStartedChild(
                        child.Machine,
                        SnapshotOwnerKind.System,
                        child.Id,
                        child.RowId
                    )
                );
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM trax.metadata WHERE external_id = {ran}"
            );
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM trax.work_queue WHERE external_id = {queued}"
            );
        }
    }

    [Test]
    public async Task A_users_draft_shows_its_own_history_only_and_needs_its_row_id()
    {
        var id = Guid.NewGuid();
        var mine = await Seed(SnapshotOwnerKind.User, "Locked");
        var at = DateTimeOffset.UtcNow;
        await History(
            mine,
            Row(
                1,
                SnapshotTransitionKind.Start,
                null,
                null,
                "Locked",
                at,
                actor: SnapshotTransitionActor.Owner
            )
        );
        var other = await Seed(SnapshotOwnerKind.User, "Locked");
        await History(
            other,
            Row(1, SnapshotTransitionKind.Start, null, null, "Locked", at),
            Row(2, SnapshotTransitionKind.Save, null, "Locked", "Locked", at)
        );

        var page = await _operations.GetMachineInstanceTransitionsAsync(
            KeyOf(mine),
            null,
            25,
            CancellationToken.None
        );
        var noRow = () =>
            _operations.GetMachineInstanceTransitionsAsync(
                new MachineInstanceKey(_machine, SnapshotOwnerKind.User, id),
                null,
                25,
                CancellationToken.None
            );
        var missing = await _operations.GetMachineInstanceTransitionsAsync(
            new MachineInstanceKey(_machine, SnapshotOwnerKind.System, Guid.NewGuid()),
            null,
            25,
            CancellationToken.None
        );

        page!.Items.Should().ContainSingle("the history is the row's, never another user's");
        page.Items[0].Actor.Should().Be(SnapshotTransitionActor.Owner);
        await noRow
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*row id*", "an operator reads one user's draft, never every user's");
        missing.Should().BeNull("no row matches");
    }

    [Test]
    public void No_transition_an_operator_reads_carries_a_context_a_user_key_or_a_token()
    {
        foreach (
            var type in new[]
            {
                typeof(MachineInstanceTransitionRecord),
                typeof(MachineInstanceStartedRun),
                typeof(MachineInstanceStartedChild),
            }
        )
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .Should()
                .NotContain(
                    ["Context", "UserKey", "InvokeToken", "SpawnedBy"],
                    $"{type.Name} names what moved an instance and what it started, never its data, its user "
                        + "or a token (central docs/0046)"
                );
    }
}
