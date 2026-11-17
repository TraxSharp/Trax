using System.Globalization;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Trax.Core.Extensions;
using Trax.Effect.Data.Sqlite.Utils;
using Trax.Effect.Data.Testing;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="TraxInvariants"/> on SQLite, over a file the shipped SQLite migrations built: every
/// check the Postgres suite seeds a violation for reports the same violation here, with SQLite's
/// table names, and the legitimate neighbour of each stays quiet. The SQLite store stores its
/// enums as numbers, so these tests are what keeps the SQLite dialect of each check honest.
/// </summary>
[TestFixture]
public class SqliteTraxInvariantsTests
{
    private const string Machine = "Invariants.Machine";

    private string? _file;
    private SqliteConnection? _connection;

    [SetUp]
    public async Task CreateDatabase()
    {
        _file = Path.Combine(Path.GetTempPath(), $"trax_invariants_{Guid.NewGuid():N}.db");
        await DatabaseMigrator.Migrate($"Data Source={_file.NotNull()}");
        _connection = new SqliteConnection($"Data Source={_file.NotNull()}");
        await _connection.NotNull().OpenAsync();
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        await _connection.NotNull().DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(_file.NotNull() + suffix);
    }

    [Test]
    public async Task ACleanDatabase_IsConsistent()
    {
        await SeedRunAsync(TrainStateNumber.Completed);
        await SeedRunAsync(TrainStateNumber.Pending);
        await SeedClaimAsync("effect-done", receipt: "sent");
        await SeedQueueEntryAsync(
            StatusNumber.Dispatched,
            await SeedRunAsync(TrainStateNumber.Failed)
        );
        await SeedQueueEntryAsync(StatusNumber.Cancelled, runId: null);
        var failed = await SeedRunAsync(TrainStateNumber.Failed);
        await SeedCheckpointAsync(failed);
        await SeedQueueEntryAsync(StatusNumber.Queued, runId: null, resumeFrom: failed);
        await SeedRunAsync(TrainStateNumber.Pending, resumeFrom: failed);
        var live = Guid.NewGuid();
        await SeedSnapshotAsync(live, "Building", await SeedInvokedEntryAsync(live, runId: null));
        await SeedSnapshotAsync(Guid.NewGuid(), "Built", token: null);

        (
            await TraxInvariants.FindViolationsAsync(
                _connection.NotNull(),
                [new InvokingState(Machine, "Building")]
            )
        )
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task EachSeededViolation_IsReportedWithItsTableAndId()
    {
        var stuck = await SeedRunAsync(TrainStateNumber.InProgress);
        await SeedClaimAsync("effect-abandoned", receipt: null);
        var orphan = await SeedQueueEntryAsync(StatusNumber.Dispatched, runId: null);
        var completed = await SeedRunAsync(TrainStateNumber.Completed);
        var kept = await SeedCheckpointAsync(completed);
        var gone = await SeedRunAsync(TrainStateNumber.Failed);
        await ExecAsync(
            string.Create(CultureInfo.InvariantCulture, $"DELETE FROM metadata WHERE id = {gone}")
        );
        var resumingEntry = await SeedQueueEntryAsync(
            StatusNumber.Queued,
            runId: null,
            resumeFrom: gone
        );
        var resumingRun = await SeedRunAsync(TrainStateNumber.Pending, resumeFrom: gone);
        var forged = await SeedSnapshotAsync(Guid.NewGuid(), "Building", "no-such-run");

        var violations = await TraxInvariants.FindViolationsAsync(_connection.NotNull(), []);

        violations
            .Select(v => (v.Invariant, v.Table, v.Id))
            .Should()
            .Equal(
                (
                    TraxInvariants.RunInProgress,
                    "metadata",
                    stuck.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (TraxInvariants.EffectClaimInFlight, "effect_claim", "effect-abandoned"),
                (
                    TraxInvariants.DispatchedWithoutRun,
                    "work_queue",
                    orphan.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (
                    TraxInvariants.CheckpointOfCompletedRun,
                    "checkpoint",
                    kept.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (
                    TraxInvariants.ResumeFromMissingRun,
                    "work_queue",
                    resumingEntry.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (
                    TraxInvariants.ResumeFromMissingRun,
                    "metadata",
                    resumingRun.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (
                    TraxInvariants.InvokeTokenWithoutRun,
                    "snapshot_draft",
                    forged.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            );
        violations[0]
            .Detail.Should()
            .Be(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"run {stuck} of 'Invariants.Train' is still in progress"
                )
            );
        violations[3]
            .Detail.Should()
            .Be(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"checkpoint {kept} at Checkpoint<Findings>#0 of run {completed} of "
                ) + "Invariants.Train is kept, but that run completed"
            );
    }

    [Test]
    public async Task AnInvokingStateWithoutAToken_AndATokenOutsideOne_AreReported()
    {
        var bare = await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null);
        var left = Guid.NewGuid();
        var stale = await SeedSnapshotAsync(
            left,
            "Built",
            await SeedInvokedEntryAsync(left, runId: null)
        );
        await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null, machine: "Other");
        await SeedSnapshotAsync(Guid.NewGuid(), "Building", token: null, stranded: "Building");

        (await TraxInvariants.FindViolationsAsync(_connection.NotNull(), []))
            .Should()
            .BeEmpty("without the machine's invoking states, neither can be told");

        var violations = await TraxInvariants.FindViolationsAsync(
            _connection.NotNull(),
            [new InvokingState(Machine, "Building")]
        );

        violations
            .Select(v => (v.Invariant, v.Id))
            .Should()
            .Equal(
                (
                    TraxInvariants.InvokingStateWithoutToken,
                    bare.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ),
                (
                    TraxInvariants.InvokeTokenOutsideInvokingState,
                    stale.ToString(System.Globalization.CultureInfo.InvariantCulture)
                )
            );
    }

    // The numbers SQLite stores TrainState and WorkQueueStatus as.
    private static class TrainStateNumber
    {
        public const int Pending = 0;
        public const int Completed = 1;
        public const int Failed = 2;
        public const int InProgress = 3;
    }

    private static class StatusNumber
    {
        public const int Queued = 0;
        public const int Dispatched = 1;
        public const int Cancelled = 2;
    }

    private async Task<long> SeedRunAsync(int state, long? resumeFrom = null) =>
        await ScalarAsync(
                """
                INSERT INTO metadata (external_id, name, train_state, start_time, resume_from)
                VALUES ($external, 'Invariants.Train', $state, datetime('now'), $resume)
                RETURNING id
                """,
                ("$external", Guid.NewGuid().ToString("N")),
                ("$state", state),
                ("$resume", resumeFrom)
            )
            .ConfigureAwait(false);

    private async Task<long> SeedCheckpointAsync(long runId) =>
        await ScalarAsync(
                """
                INSERT INTO checkpoint
                    (metadata_id, node_id, state_type, state, chain_hash, state_fingerprint)
                VALUES ($run, 'Checkpoint<Findings>#0', 'Findings', '{}', 'hash', 'fp')
                RETURNING id
                """,
                ("$run", runId)
            )
            .ConfigureAwait(false);

    private async Task<long> SeedQueueEntryAsync(
        int status,
        long? runId,
        long? resumeFrom = null
    ) =>
        await ScalarAsync(
                """
                INSERT INTO work_queue (external_id, train_name, status, metadata_id, resume_from)
                VALUES ($external, 'Invariants.Train', $status, $run, $resume)
                RETURNING id
                """,
                ("$external", Guid.NewGuid().ToString("N")),
                ("$status", status),
                ("$run", runId),
                ("$resume", resumeFrom)
            )
            .ConfigureAwait(false);

    [Test]
    public async Task TheLifetimeCheck_CountsAsTheSweepDoes()
    {
        // Built is kept however old; a row with no creation time counts from its last write; a draft waiting on
        // its run is bounded too.
        await SeedUserDraftAsync("Built", "datetime('now', '-3 days')", "datetime('now')");
        var editable = await SeedUserDraftAsync(
            "Editing",
            "datetime('now', '-3 days')",
            "datetime('now')"
        );
        var unborn = await SeedUserDraftAsync("Editing", "NULL", "datetime('now', '-3 days')");
        await SeedUserDraftAsync("Editing", "NULL", "datetime('now')");
        var waiting = Guid.NewGuid();
        var running = await SeedUserDraftAsync(
            "Building",
            "datetime('now', '-3 days')",
            "datetime('now')",
            await SeedInvokedEntryAsync(waiting, runId: null)
        );

        var violations = (
            await TraxInvariants.FindViolationsAsync(
                _connection.NotNull(),
                [],
                [],
                maxDraftLifetime: TimeSpan.FromDays(1),
                keptStates: [new KeptState(Machine, "Built")]
            )
        )
            .Where(v =>
                string.Equals(
                    v.Invariant,
                    TraxInvariants.DraftOlderThanMaxLifetime,
                    StringComparison.Ordinal
                )
            )
            .Select(v => v.Id);

        violations
            .Should()
            .BeEquivalentTo([
                editable.ToString(System.Globalization.CultureInfo.InvariantCulture),
                unborn.ToString(System.Globalization.CultureInfo.InvariantCulture),
                running.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ]);
    }

    // A user's draft in state, with created_at and updated_at as SQL expressions; returns its row id.
    private async Task<long> SeedUserDraftAsync(
        string state,
        string createdAt,
        string updatedAt,
        string? token = null
    ) =>
        await ScalarAsync(
                $"""
                INSERT INTO snapshot_draft
                    (id, user_key, owner_kind, machine, version, state, concurrency_token, created_at, updated_at,
                     invoke_token)
                VALUES ($id, 'u1', 0, $machine, 1, $state, $concurrency, {createdAt}, {updatedAt}, $token)
                RETURNING row_id
                """,
                ("$id", Guid.NewGuid().ToString()),
                ("$machine", Machine),
                ("$state", state),
                ("$concurrency", Guid.NewGuid().ToString()),
                ("$token", token)
            )
            .ConfigureAwait(false);

    private async Task<string> SeedInvokedEntryAsync(Guid instance, long? runId)
    {
        var external = Guid.NewGuid().ToString("N");
        await ScalarAsync(
                """
                INSERT INTO work_queue
                    (external_id, train_name, status, metadata_id,
                     invoking_machine, invoking_instance_id, invoking_owner_kind)
                VALUES ($external, 'Invariants.Train', $status, $run, $machine, $instance, 1)
                RETURNING id
                """,
                ("$external", external),
                ("$status", runId is null ? StatusNumber.Queued : StatusNumber.Dispatched),
                ("$run", runId),
                ("$machine", Machine),
                ("$instance", instance.ToString())
            )
            .ConfigureAwait(false);
        return external;
    }

    private async Task<long> SeedSnapshotAsync(
        Guid id,
        string state,
        string? token,
        string machine = Machine,
        string? stranded = null
    ) =>
        await ScalarAsync(
                """
                INSERT INTO snapshot_draft
                    (id, user_key, owner_kind, machine, version, state, concurrency_token, updated_at,
                     invoke_token, invoke_stranded_state)
                VALUES ($id, NULL, 1, $machine, 1, $state, $concurrency, datetime('now'), $token, $stranded)
                RETURNING row_id
                """,
                ("$id", id.ToString()),
                ("$machine", machine),
                ("$state", state),
                ("$concurrency", Guid.NewGuid().ToString()),
                ("$token", token),
                ("$stranded", stranded)
            )
            .ConfigureAwait(false);

    private async Task SeedClaimAsync(string key, string? receipt) =>
        await ScalarAsync(
                """
                INSERT INTO effect_claim (effect_key, receipt, owner_token, lease_expires_at, created_at)
                VALUES ($key, $receipt, $owner, datetime('now'), datetime('now'))
                RETURNING 0
                """,
                ("$key", key),
                ("$receipt", receipt),
                ("$owner", Guid.NewGuid().ToString())
            )
            .ConfigureAwait(false);

    private async Task ExecAsync(string sql)
    {
        var command = _connection.NotNull().CreateCommand();
        await using var commandDisposal = command.ConfigureAwait(false);
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private async Task<long> ScalarAsync(string sql, params (string Name, object? Value)[] values)
    {
        var command = _connection.NotNull().CreateCommand();
        await using var commandDisposal = command.ConfigureAwait(false);
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false)).NotNull();
    }
}
