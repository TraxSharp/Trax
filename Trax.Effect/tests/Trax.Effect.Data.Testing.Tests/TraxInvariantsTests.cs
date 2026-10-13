using Npgsql;
using Trax.Effect.Data.Postgres.Utils;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// Each of <see cref="TraxInvariants"/>' checks against a real database built by the shipped
/// migrations: it reports a seeded violation with its id, stays quiet on the legitimate neighbour
/// of that violation, and a clean database passes. A throwaway database keeps the seeded rows
/// away from every other suite.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TraxInvariantsTests
{
    private const string Database = "trax_invariants_tests";

    private static readonly int Port = int.TryParse(
        Environment.GetEnvironmentVariable("TRAX_TEST_PG_PORT"),
        out var port
    )
        ? port
        : 5432;

    private static readonly string Maintenance =
        $"Host=localhost;Port={Port};Username=trax;Password=trax123;Database=postgres";

    private static readonly string ConnectionString =
        $"Host=localhost;Port={Port};Username=trax;Password=trax123;Database={Database};Include Error Detail=true";

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        await using (var admin = new NpgsqlConnection(Maintenance))
        {
            await admin.OpenAsync();
            await ExecAsync(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
            await ExecAsync(admin, $"CREATE DATABASE {Database}");
        }

        await DatabaseMigrator.Migrate(ConnectionString);
    }

    [OneTimeTearDown]
    public async Task DropDatabase()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await ExecAsync(admin, $"DROP DATABASE IF EXISTS {Database} WITH (FORCE)");
    }

    [SetUp]
    public Task Clean() =>
        SqlAsync(
            "DELETE FROM trax.snapshot_draft; DELETE FROM trax.work_queue; "
                + "DELETE FROM trax.effect_claim; DELETE FROM trax.metadata;"
        );

    [Test]
    public async Task ACleanDatabase_IsConsistent()
    {
        await SeedRunAsync("completed");
        await SeedClaimAsync("effect-done", receipt: "sent");
        await SeedQueueEntryAsync("dispatched", await SeedRunAsync("failed"));
        await SeedQueueEntryAsync("queued", runId: null);
        var failed = await SeedRunAsync("failed");
        await SeedCheckpointAsync(failed);
        await SeedQueueEntryAsync("queued", runId: null, resumeFrom: failed);
        await SeedRunAsync("pending", resumeFrom: failed);

        (await TraxInvariants.FindViolationsAsync(ConnectionString)).Should().BeEmpty();
        await FluentActions
            .Awaiting(() => TraxInvariants.AssertConsistentAsync(ConnectionString))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task ARunStillInProgress_IsReportedWithItsId()
    {
        var stuck = await SeedRunAsync("in_progress");
        await SeedRunAsync("pending");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .ContainSingle("a pending run has not started, so nothing was lost")
            .Which.Should()
            .Be(
                new TraxInvariantViolation(
                    TraxInvariants.RunInProgress,
                    "trax.metadata",
                    stuck.ToString(),
                    $"run {stuck} of 'Invariants.Train' is still in progress"
                )
            );
    }

    [Test]
    public async Task AnEffectClaimWithNoReceipt_IsReportedWithItsKey()
    {
        await SeedClaimAsync("effect-abandoned", receipt: null);
        await SeedClaimAsync("effect-done", receipt: "sent");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.EffectClaimInFlight
                && v.Table == "trax.effect_claim"
                && v.Id == "effect-abandoned"
            );
    }

    [Test]
    public async Task ADispatchedEntryWithNoRun_IsReportedWithItsId()
    {
        var orphan = await SeedQueueEntryAsync("dispatched", runId: null);
        await SeedQueueEntryAsync("dispatched", await SeedRunAsync("completed"));
        await SeedQueueEntryAsync("cancelled", runId: null);

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .ContainSingle(
                "a cancelled entry never had a run, and the other dispatched one has its run"
            )
            .Which.Should()
            .Be(
                new TraxInvariantViolation(
                    TraxInvariants.DispatchedWithoutRun,
                    "trax.work_queue",
                    orphan.ToString(),
                    $"queue entry {orphan} is dispatched but its run (null) does not exist"
                )
            );
    }

    [Test]
    public async Task ACheckpointOfACompletedRun_IsReportedWithItsId()
    {
        var completed = await SeedRunAsync("completed");
        var kept = await SeedCheckpointAsync(completed);
        await SeedCheckpointAsync(await SeedRunAsync("failed"));
        await SeedRunAsync("completed");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .ContainSingle(
                "a failed run's checkpoint is what a resume reads, and a completed run with none is clean"
            )
            .Which.Should()
            .Be(
                new TraxInvariantViolation(
                    TraxInvariants.CheckpointOfCompletedRun,
                    "trax.checkpoint",
                    kept.ToString(),
                    $"checkpoint {kept} at Checkpoint<Findings>#0 of run {completed} of "
                        + "Invariants.Train is kept, but that run completed"
                )
            );
    }

    [Test]
    public async Task AResumeNamingNoRun_IsReportedOnTheEntryAndTheRun()
    {
        var source = await SeedRunAsync("failed");
        var gone = await SeedRunAsync("failed");
        await SqlAsync($"DELETE FROM trax.metadata WHERE id = {gone}");

        var orphanEntry = await SeedQueueEntryAsync("queued", runId: null, resumeFrom: gone);
        await SeedQueueEntryAsync(
            "dispatched",
            await SeedRunAsync("completed"),
            resumeFrom: source
        );
        var orphanRun = await SeedRunAsync("pending", resumeFrom: gone);
        await SeedRunAsync("pending", resumeFrom: source);

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .BeEquivalentTo(
                [
                    new TraxInvariantViolation(
                        TraxInvariants.ResumeFromMissingRun,
                        "trax.work_queue",
                        orphanEntry.ToString(),
                        $"queue entry {orphanEntry} resumes run {gone}, which does not exist"
                    ),
                    new TraxInvariantViolation(
                        TraxInvariants.ResumeFromMissingRun,
                        "trax.metadata",
                        orphanRun.ToString(),
                        $"run {orphanRun} resumes run {gone}, which does not exist"
                    ),
                ],
                o => o.WithStrictOrdering(),
                "an entry and a run resuming a run that still exists are both fine"
            );
    }

    [Test]
    public async Task AnInvokeTokenNamingNoRunOfItsInstance_IsReportedWithItsRow()
    {
        var queued = Guid.NewGuid();
        var token = await SeedInvokedEntryAsync(queued, runId: null);
        await SeedSnapshotAsync(queued, "Building", token);

        var dispatched = Guid.NewGuid();
        var dispatchedToken = await SeedInvokedEntryAsync(
            dispatched,
            await SeedRunAsync("completed")
        );
        await SeedSnapshotAsync(dispatched, "Building", dispatchedToken);

        var onlyRun = Guid.NewGuid();
        var runToken = await SeedInvokedRunAsync(onlyRun);
        await SeedSnapshotAsync(onlyRun, "Building", runToken);

        var forged = await SeedSnapshotAsync(Guid.NewGuid(), "Building", "no-such-run");
        var borrowed = await SeedSnapshotAsync(
            Guid.NewGuid(),
            "Building",
            await SeedInvokedEntryAsync(Guid.NewGuid(), runId: null)
        );

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString);

        violations
            .Should()
            .OnlyContain(v => v.Invariant == TraxInvariants.InvokeTokenWithoutRun)
            .And.Subject.Select(v => v.Id)
            .Should()
            .Equal(
                [forged.ToString(), borrowed.ToString()],
                "a token is good when its queue entry or, once that is gone, its run names the "
                    + "instance; one naming no run, or another instance's run, is not"
            );
    }

    [Test]
    public async Task AnInvokingStateWithoutAToken_AndATokenOutsideOne_AreReported()
    {
        var invoking = new[] { new InvokingState(Machine, "Building") };

        var live = Guid.NewGuid();
        await SeedSnapshotAsync(live, "Building", await SeedInvokedEntryAsync(live, null));
        await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);
        var bare = await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null);
        var left = Guid.NewGuid();
        var stale = await SeedSnapshotAsync(left, "Built", await SeedInvokedEntryAsync(left, null));
        await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null, machine: "Other");
        // Stranded there on purpose: its run ended and not even the failure could be applied.
        await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null, stranded: "Building");
        // Stranded in a state it has since left, so the mark says nothing about this one.
        var moved = await SeedSnapshotAsync(
            Guid.NewGuid(),
            "Building",
            token: null,
            stranded: "Queued"
        );

        (await TraxInvariants.FindViolationsAsync(ConnectionString))
            .Should()
            .BeEmpty("without the machine's invoking states, neither can be told");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString, invoking);

        violations
            .Should()
            .BeEquivalentTo(
                [
                    new TraxInvariantViolation(
                        TraxInvariants.InvokingStateWithoutToken,
                        "trax.snapshot_draft",
                        bare.ToString(),
                        $"{Machine} {ReadId(bare)} is in Building, which invokes a train, and holds no invoke token"
                    ),
                    new TraxInvariantViolation(
                        TraxInvariants.InvokingStateWithoutToken,
                        "trax.snapshot_draft",
                        moved.ToString(),
                        $"{Machine} {ReadId(moved)} is in Building, which invokes a train, and holds no invoke token"
                    ),
                    new TraxInvariantViolation(
                        TraxInvariants.InvokeTokenOutsideInvokingState,
                        "trax.snapshot_draft",
                        stale.ToString(),
                        $"{Machine} {left} is in Built and holds {ReadToken(stale)}, but that state invokes nothing"
                    ),
                ],
                o => o.WithStrictOrdering(),
                "a machine the caller did not describe is not checked"
            );
    }

    private const string MachineApiAdr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly TimedState[] Timed = [new(Machine, "Review")];

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task ARevisionBelowOne_IsReportedWithItsRow()
    {
        var zero = await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);
        await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);
        // The trigger overwrites any revision an UPDATE sets, so the row is set below 1 with it switched off.
        await SqlAsync(
            $"""
            BEGIN;
            ALTER TABLE trax.snapshot_draft DISABLE TRIGGER snapshot_draft_revision;
            UPDATE trax.snapshot_draft SET revision = 0 WHERE row_id = {zero};
            ALTER TABLE trax.snapshot_draft ENABLE TRIGGER snapshot_draft_revision;
            COMMIT;
            """
        );

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString, [], Timed);

        violations
            .Should()
            .ContainSingle(
                $"every write bumps the revision from 1, so none is below it. See {MachineApiAdr}"
            )
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.RevisionBelowOne
                && v.Table == "trax.snapshot_draft"
                && v.Id == zero.ToString()
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task AUserDraftWithAnEmptyOrBlankOwner_IsReportedWithItsRow()
    {
        var empty = await SeedUserDraftAsync("");
        var blank = await SeedUserDraftAsync("   ");
        await SeedUserDraftAsync("u1");
        await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString, [], Timed);

        violations
            .Where(v => v.Invariant == TraxInvariants.UserDraftWithoutOwner)
            .Select(v => v.Id)
            .Should()
            .Equal(
                [empty.ToString(), blank.ToString()],
                $"a user's draft names its owner; a system row names none and is fine. See {MachineApiAdr}"
            );
        violations.Should().OnlyContain(v => v.Invariant == TraxInvariants.UserDraftWithoutOwner);
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task TwoRowsWithOneRowId_AreReported()
    {
        // row_id is the primary key, so the duplicate can only be made with the key dropped. It is dropped inside a
        // transaction the check reads through and that is rolled back, so the table keeps its key.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var first = await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);
        await ExecAsync(
            connection,
            "ALTER TABLE trax.snapshot_draft DROP CONSTRAINT pk_snapshot_draft"
        );
        await ExecAsync(
            connection,
            $"""
            INSERT INTO trax.snapshot_draft
                (row_id, id, user_key, owner_kind, machine, version, state, concurrency_token, updated_at)
            VALUES ({first}, gen_random_uuid(), NULL, 'system', '{Machine}', 1, 'Built', gen_random_uuid(), now())
            """
        );

        var violations = await TraxInvariants.FindViolationsAsync(connection, [], Timed);
        await transaction.RollbackAsync();

        violations
            .Should()
            .Contain(
                v => v.Invariant == TraxInvariants.DuplicateRowId && v.Id == first.ToString(),
                $"a row id names one incarnation of one instance. See {MachineApiAdr}"
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task ADueTimeInAStateWithNoTimer_IsReportedWithItsRow()
    {
        var idle = await SeedSnapshotAsync(Guid.NewGuid(), "Idle", token: null);
        await SeedSnapshotAsync(Guid.NewGuid(), "Idle", token: null);
        await SqlAsync($"UPDATE trax.snapshot_draft SET due_at = now() WHERE row_id = {idle}");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString, [], Timed);

        violations
            .Should()
            .ContainSingle($"only a timed state holds a due time. See {MachineApiAdr}")
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.DueAtOutsideTimedState && v.Id == idle.ToString()
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task ATimedStateWithNoDueTime_IsReportedWithItsRow()
    {
        var review = await SeedSnapshotAsync(Guid.NewGuid(), "Review", token: null);
        await SeedSnapshotAsync(Guid.NewGuid(), "Idle", token: null);
        // The same state of a machine the caller did not describe is not checked.
        await SeedSnapshotAsync(Guid.NewGuid(), "Review", token: null, machine: "Other");

        (await TraxInvariants.FindViolationsAsync(ConnectionString))
            .Should()
            .BeEmpty("without the machine's timed states, nothing can be told");

        var violations = await TraxInvariants.FindViolationsAsync(ConnectionString, [], Timed);

        violations
            .Should()
            .ContainSingle(
                $"every entry into a timed state writes its due time. See {MachineApiAdr}"
            )
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.TimedStateWithoutDueAt && v.Id == review.ToString()
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task ADueTimeOfAMachineWithNoTimerLeft_IsReportedOnceTheHostsStop()
    {
        // Read here as a machine described by its invoking states but by no timed state: every timer it had is
        // gone, so a due time it still holds is one the sweep has not cleared yet.
        var stale = await SeedSnapshotAsync(
            Guid.NewGuid(),
            "Building",
            token: null,
            machine: "Other"
        );
        await SqlAsync($"UPDATE trax.snapshot_draft SET due_at = now() WHERE row_id = {stale}");

        var violations = await TraxInvariants.FindViolationsAsync(
            ConnectionString,
            [new InvokingState("Other", "Queued")],
            Timed
        );

        violations
            .Should()
            .ContainSingle(
                $"a stale due time is allowed only until the next sweep, so none is left once the hosts stop. See {MachineApiAdr}"
            )
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.DueAtOfARemovedTimer && v.Id == stale.ToString()
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task AUserDraftOlderThanTheMaximumLifetime_IsReportedWithItsRow()
    {
        var old = await SeedUserDraftAsync("u1", createdAt: DateTimeOffset.UtcNow.AddDays(-3));
        await SeedUserDraftAsync("u2", createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        // A system instance lives as long as the system keeps it.
        await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);
        await SqlAsync(
            "UPDATE trax.snapshot_draft SET created_at = now() - interval '30 days' WHERE owner_kind = 'system'"
        );

        (await TraxInvariants.FindViolationsAsync(ConnectionString, [], Timed))
            .Should()
            .BeEmpty("with no maximum lifetime, no draft is too old");

        var violations = await TraxInvariants.FindViolationsAsync(
            ConnectionString,
            [],
            Timed,
            maxDraftLifetime: TimeSpan.FromDays(1)
        );

        violations
            .Should()
            .ContainSingle(
                $"a pending timer never keeps a draft past its maximum lifetime. See {MachineApiAdr}"
            )
            .Which.Should()
            .Match<TraxInvariantViolation>(v =>
                v.Invariant == TraxInvariants.DraftOlderThanMaxLifetime && v.Id == old.ToString()
            );
    }

    [Test]
    [Property("adr", MachineApiAdr)]
    public async Task TheLifetimeCheck_CountsAsTheSweepDoes()
    {
        var longAgo = DateTimeOffset.UtcNow.AddDays(-3);
        // Built is kept by the sweep however old: the record of what the server did.
        await SeedUserDraftAsync("u1", createdAt: longAgo, state: "Built");
        var editable = await SeedUserDraftAsync("u1", createdAt: longAgo, state: "Editing");
        // No creation time recorded: the age counts from the last write.
        var unborn = await SeedUserDraftAsync(
            "u2",
            state: "Editing",
            noCreatedAt: true,
            updatedAt: longAgo
        );
        await SeedUserDraftAsync("u2", state: "Editing", noCreatedAt: true);
        // A draft waiting on its run is bounded too: the sweep cancels the run and deletes it.
        var waiting = Guid.NewGuid();
        var running = await SeedUserDraftAsync(
            "u3",
            createdAt: longAgo,
            state: "Building",
            token: await SeedInvokedEntryAsync(waiting, null)
        );

        var violations = (
            await TraxInvariants.FindViolationsAsync(
                ConnectionString,
                [],
                Timed,
                maxDraftLifetime: TimeSpan.FromDays(1),
                keptStates: [new KeptState(Machine, "Built")]
            )
        )
            .Where(v => v.Invariant == TraxInvariants.DraftOlderThanMaxLifetime)
            .Select(v => v.Id)
            .ToList();

        violations
            .Should()
            .BeEquivalentTo(
                [editable.ToString(), unborn.ToString(), running.ToString()],
                $"the check reports exactly the drafts the sweep deletes for their age. See {MachineApiAdr}"
            );
    }

    // A user's draft owned by userKey in state, created at createdAt (now when null, none recorded with
    // noCreatedAt), last written at updatedAt (now when null), waiting on the run token names; returns its row id.
    private static async Task<long> SeedUserDraftAsync(
        string userKey,
        DateTimeOffset? createdAt = null,
        string state = "Built",
        bool noCreatedAt = false,
        DateTimeOffset? updatedAt = null,
        string? token = null
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.snapshot_draft
                (id, user_key, owner_kind, machine, version, state, concurrency_token, created_at, updated_at,
                 invoke_token)
            VALUES (gen_random_uuid(), @user, 'user', @machine, 1, @state, gen_random_uuid(), @created, @updated,
                    @token)
            RETURNING row_id
            """;
        command.Parameters.AddWithValue("user", userKey);
        command.Parameters.AddWithValue("machine", Machine);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue(
            "created",
            noCreatedAt ? DBNull.Value : (object)(createdAt ?? DateTimeOffset.UtcNow)
        );
        command.Parameters.AddWithValue("updated", updatedAt ?? DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("token", (object?)token ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task AssertConsistent_ReportsEveryViolation_NotOnlyTheFirst()
    {
        var first = await SeedRunAsync("in_progress");
        var second = await SeedRunAsync("in_progress");
        await SeedClaimAsync("effect-abandoned", receipt: null);
        var orphan = await SeedQueueEntryAsync("dispatched", runId: null);

        var thrown = await FluentActions
            .Awaiting(() => TraxInvariants.AssertConsistentAsync(ConnectionString))
            .Should()
            .ThrowAsync<InvalidOperationException>();

        thrown
            .Which.Message.Should()
            .Contain("4 violation(s)")
            .And.Contain($"[run-in-progress] trax.metadata {first}:")
            .And.Contain($"[run-in-progress] trax.metadata {second}:")
            .And.Contain("[effect-claim-in-flight] trax.effect_claim effect-abandoned:")
            .And.Contain($"[dispatched-without-run] trax.work_queue {orphan}:")
            .And.Contain("[LeavesStuckRuns(");
    }

    [Test]
    public void IsExempt_ReadsTheAttribute_OnTheMethodOrTheFixture()
    {
        TraxInvariants.IsExempt(typeof(Marked), nameof(Marked.Stuck)).Should().BeTrue();
        TraxInvariants.IsExempt(typeof(Marked), nameof(Marked.Clean)).Should().BeFalse();
        TraxInvariants.IsExempt(typeof(Marked), null).Should().BeFalse();
        TraxInvariants
            .IsExempt(typeof(MarkedFixture), nameof(MarkedFixture.Anything))
            .Should()
            .BeTrue();
        TraxInvariants
            .IsExempt(typeof(InheritsMarkedFixture), nameof(MarkedFixture.Anything))
            .Should()
            .BeTrue("a fixture's base class can carry the mark for every subclass");
    }

    private class Marked
    {
        [LeavesStuckRuns("simulates a host crashing mid-run")]
        public void Stuck() { }

        public void Clean() { }
    }

    [LeavesStuckRuns("every test here abandons a claim")]
    private class MarkedFixture
    {
        public void Anything() { }
    }

    private sealed class InheritsMarkedFixture : MarkedFixture;

    private static async Task<long> SeedRunAsync(string state, long? resumeFrom = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.metadata (external_id, name, train_state, start_time, resume_from)
            VALUES (@external, 'Invariants.Train', @state::trax.train_state, now(), @resume)
            RETURNING id
            """;
        command.Parameters.AddWithValue("external", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue(
            "resume",
            NpgsqlTypes.NpgsqlDbType.Bigint,
            (object?)resumeFrom ?? DBNull.Value
        );
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // A checkpoint row of run <paramref name="runId"/>; returns its id.
    private static async Task<long> SeedCheckpointAsync(long runId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.checkpoint
                (metadata_id, node_id, state_type, state, chain_hash, state_fingerprint)
            VALUES (@run, 'Checkpoint<Findings>#0', 'Findings', '{}', repeat('a', 64), 'fp')
            RETURNING id
            """;
        command.Parameters.AddWithValue("run", runId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private const string Machine = "Invariants.Machine";

    private static readonly Dictionary<long, (Guid Id, string? Token)> Snapshots = [];

    private static Guid ReadId(long row) => Snapshots[row].Id;

    private static string? ReadToken(long row) => Snapshots[row].Token;

    private static async Task<long> SeedSnapshotAsync(
        Guid id,
        string state,
        string? token,
        string machine = Machine,
        string? stranded = null
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.snapshot_draft
                (id, user_key, owner_kind, machine, version, state, concurrency_token, updated_at, invoke_token,
                 invoke_stranded_state)
            VALUES (@id, NULL, 'system', @machine, 1, @state, gen_random_uuid(), now(), @token, @stranded)
            RETURNING row_id
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("machine", machine);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("token", (object?)token ?? DBNull.Value);
        command.Parameters.AddWithValue("stranded", (object?)stranded ?? DBNull.Value);
        var row = (long)(await command.ExecuteScalarAsync())!;
        Snapshots[row] = (id, token);
        return row;
    }

    // A queue entry an invoking state wrote for instance <paramref name="instance"/>; returns its
    // external id, which is the instance's token.
    private static async Task<string> SeedInvokedEntryAsync(Guid instance, long? runId)
    {
        var external = Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.work_queue
                (external_id, train_name, status, metadata_id,
                 invoking_machine, invoking_instance_id, invoking_owner_kind)
            VALUES (@external, 'Invariants.Train', @status::trax.work_queue_status, @run,
                    @machine, @instance, 'system')
            """;
        command.Parameters.AddWithValue("external", external);
        command.Parameters.AddWithValue("status", runId is null ? "queued" : "dispatched");
        command.Parameters.AddWithValue("run", (object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue("machine", Machine);
        command.Parameters.AddWithValue("instance", instance);
        await command.ExecuteNonQueryAsync();
        return external;
    }

    // A run of instance <paramref name="instance"/> whose queue entry has since been deleted;
    // returns its external id, which is the instance's token.
    private static async Task<string> SeedInvokedRunAsync(Guid instance)
    {
        var external = Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.metadata
                (external_id, name, train_state, start_time,
                 invoking_machine, invoking_instance_id, invoking_owner_kind)
            VALUES (@external, 'Invariants.Train', 'completed'::trax.train_state, now(),
                    @machine, @instance, 'system')
            """;
        command.Parameters.AddWithValue("external", external);
        command.Parameters.AddWithValue("machine", Machine);
        command.Parameters.AddWithValue("instance", instance);
        await command.ExecuteNonQueryAsync();
        return external;
    }

    private static async Task SeedClaimAsync(string key, string? receipt)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.effect_claim (effect_key, receipt, owner_token, lease_expires_at, created_at)
            VALUES (@key, @receipt, @owner, now(), now())
            """;
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("receipt", (object?)receipt ?? DBNull.Value);
        command.Parameters.AddWithValue("owner", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> SeedQueueEntryAsync(
        string status,
        long? runId,
        long? resumeFrom = null
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.work_queue (external_id, train_name, status, metadata_id, resume_from)
            VALUES (@external, 'Invariants.Train', @status::trax.work_queue_status, @run, @resume)
            RETURNING id
            """;
        command.Parameters.AddWithValue("external", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("run", (object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "resume",
            NpgsqlTypes.NpgsqlDbType.Bigint,
            (object?)resumeFrom ?? DBNull.Value
        );
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecAsync(connection, sql);
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
