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

    private static async Task<long> SeedRunAsync(string state)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.metadata (external_id, name, train_state, start_time)
            VALUES (@external, 'Invariants.Train', @state::trax.train_state, now())
            RETURNING id
            """;
        command.Parameters.AddWithValue("external", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("state", state);
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
        string machine = Machine
    )
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.snapshot_draft
                (id, user_key, owner_kind, machine, version, state, concurrency_token, updated_at, invoke_token)
            VALUES (@id, NULL, 'system', @machine, 1, @state, gen_random_uuid(), now(), @token)
            RETURNING row_id
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("machine", machine);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("token", (object?)token ?? DBNull.Value);
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

    private static async Task<long> SeedQueueEntryAsync(string status, long? runId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trax.work_queue (external_id, train_name, status, metadata_id)
            VALUES (@external, 'Invariants.Train', @status::trax.work_queue_status, @run)
            RETURNING id
            """;
        command.Parameters.AddWithValue("external", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("run", (object?)runId ?? DBNull.Value);
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
