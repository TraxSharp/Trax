using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Npgsql;
using Trax.Effect.Enums;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Checks that a Trax Postgres or SQLite database is consistent once every host using it has stopped: no run
/// is still in progress, no state-machine effect is still claimed in flight, every dispatched
/// queue entry has its run, no completed run keeps a checkpoint, every resume names a run that
/// exists, and every machine instance's invoke token names a run that instance queued. While a
/// host runs, the first three are normal transient states; once they have all stopped, each one
/// is work that was started and then lost. The rest hold whenever no write is half done: each is
/// kept by the one statement or transaction that changes what it relates.
/// </summary>
/// <remarks>
/// <para>Call it from a test fixture's teardown, after the hosts the test started are disposed. A
/// test that leaves one of these states on purpose (a crash simulation, an abandoned claim) carries
/// <see cref="LeavesStuckRunsAttribute"/>, and the fixture skips the check for it with
/// <see cref="IsExempt"/>.</para>
/// <para>The database does not know which states invoke a train, so whether an instance in an
/// invoking state holds a token, and only one in such a state does, is checked only for the
/// machines whose invoking states the caller passes as <see cref="InvokingState"/>s.</para>
/// <para>It reads the tables the shipped migrations create: the <c>trax</c> schema on Postgres, by
/// connection string or open connection, and the same tables on SQLite, by open connection. The
/// checks are the same on both.</para>
/// </remarks>
public static class TraxInvariants
{
    /// <summary>A <c>trax.metadata</c> row is still <c>in_progress</c>.</summary>
    public const string RunInProgress = "run-in-progress";

    /// <summary>A <c>trax.effect_claim</c> row has no receipt: its effect was claimed and neither completed nor released.</summary>
    public const string EffectClaimInFlight = "effect-claim-in-flight";

    /// <summary>A <c>trax.work_queue</c> row is <c>dispatched</c> but names no run that exists.</summary>
    public const string DispatchedWithoutRun = "dispatched-without-run";

    /// <summary>
    /// A <c>trax.checkpoint</c> row belongs to a run that completed. A run that completes deletes
    /// its checkpoints, since nothing may resume it.
    /// </summary>
    public const string CheckpointOfCompletedRun = "checkpoint-of-completed-run";

    /// <summary>
    /// A <c>trax.work_queue</c> or <c>trax.metadata</c> row's <c>resume_from</c> names no run that
    /// exists. The metadata cleanup keeps a run while something resumes it.
    /// </summary>
    public const string ResumeFromMissingRun = "resume-from-missing-run";

    /// <summary>
    /// A <c>trax.snapshot_draft</c> row holds an invoke token that names no run the instance
    /// queued: neither a queue entry nor a run with that id carries the instance as its invoker.
    /// </summary>
    public const string InvokeTokenWithoutRun = "invoke-token-without-run";

    /// <summary>
    /// A <c>trax.snapshot_draft</c> row is in a state that invokes a train and holds no invoke
    /// token, so no outcome can ever move it on, and was not stranded there on purpose. A row whose
    /// <c>invoke_stranded_state</c> is its state is exempt: its run ended and not even the failure
    /// could be applied, so the token was cleared and the instance left to leave the state through
    /// one of its declared transitions.
    /// </summary>
    public const string InvokingStateWithoutToken = "invoking-state-without-token";

    /// <summary>
    /// A <c>trax.snapshot_draft</c> row holds an invoke token in a state that invokes nothing:
    /// leaving the invoking state did not clear it.
    /// </summary>
    public const string InvokeTokenOutsideInvokingState = "invoke-token-outside-invoking-state";

    /// <summary>
    /// Every violation in the Postgres database at <paramref name="connectionString"/>, in a stable
    /// order: by invariant, then by id.
    /// </summary>
    public static Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    ) => FindViolationsAsync(connectionString, [], cancellationToken);

    /// <summary>
    /// Every violation in the Postgres database at <paramref name="connectionString"/>, in a stable
    /// order: by invariant, then by id. Instances of a machine named in
    /// <paramref name="invokingStates"/> are also checked to hold a token exactly when they are in
    /// one of its invoking states.
    /// </summary>
    /// <param name="connectionString">The Postgres database to check.</param>
    /// <param name="invokingStates">
    /// Every state that invokes a train, for each machine to check. A machine with no entry here
    /// is checked only for tokens that name no run.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public static async Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(
        string connectionString,
        IEnumerable<InvokingState> invokingStates,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(invokingStates);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await FindViolationsAsync(connection, invokingStates, cancellationToken);
    }

    /// <summary>
    /// Every violation in the database <paramref name="connection"/> is open on, a Postgres or a
    /// SQLite one, in a stable order: by invariant, then by id. The checks are the same on both;
    /// only their SQL differs, and on SQLite a violation names its table without the schema.
    /// </summary>
    /// <param name="connection">
    /// An open <c>NpgsqlConnection</c>, or an open <c>Microsoft.Data.Sqlite.SqliteConnection</c> on
    /// a database the shipped SQLite migrations built. The caller keeps owning it.
    /// </param>
    /// <param name="invokingStates">
    /// Every state that invokes a train, for each machine to check. A machine with no entry here
    /// is checked only for tokens that name no run.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="NotSupportedException">The connection is to neither Postgres nor SQLite.</exception>
    public static async Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(
        DbConnection connection,
        IEnumerable<InvokingState> invokingStates,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(invokingStates);

        var dialect = Dialect.Of(connection);
        var invoking = invokingStates.Distinct().ToList();
        var violations = new List<TraxInvariantViolation>();

        // A run a host had started and never finished. After every host has stopped, nothing will
        // finish it: stuck-job recovery is the only thing that would move it, and only later.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("id")}, name
                FROM {dialect.Table("metadata")}
                WHERE train_state = {dialect.State(TrainState.InProgress)}
                ORDER BY id
                """,
                (id, name) =>
                    new TraxInvariantViolation(
                        RunInProgress,
                        dialect.Table("metadata"),
                        id,
                        $"run {id} of '{name}' is still in progress"
                    ),
                cancellationToken
            )
        );

        // A claim names its effect's intent, not a run, so it cannot be joined to one. A receipt is
        // written when the effect completes and the claim is deleted when it is released, so a
        // claim with neither is an effect whose outcome nobody recorded.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT effect_key, {dialect.Text("owner_token")}
                FROM {dialect.Table("effect_claim")}
                WHERE receipt IS NULL
                ORDER BY effect_key
                """,
                (key, owner) =>
                    new TraxInvariantViolation(
                        EffectClaimInFlight,
                        dialect.Table("effect_claim"),
                        key,
                        $"effect '{key}' is claimed by {owner} with no receipt"
                    ),
                cancellationToken
            )
        );

        // The dispatcher creates the run and marks the entry dispatched in one transaction, so a
        // dispatched entry with no run means that write was split or its run deleted after it.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("w.id")}, coalesce({dialect.Text("w.metadata_id")}, 'null')
                FROM {dialect.Table("work_queue")} w
                LEFT JOIN {dialect.Table("metadata")} m ON m.id = w.metadata_id
                WHERE w.status = {dialect.Status(WorkQueueStatus.Dispatched)} AND m.id IS NULL
                ORDER BY w.id
                """,
                (id, run) =>
                    new TraxInvariantViolation(
                        DispatchedWithoutRun,
                        dialect.Table("work_queue"),
                        id,
                        $"queue entry {id} is dispatched but its run ({run}) does not exist"
                    ),
                cancellationToken
            )
        );

        // A run that completes deletes its own checkpoints in its terminal write: nothing may
        // resume it, and a row left behind is run data kept for no purpose.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("c.id")}, c.node_id || ' of run ' || {dialect.Text(
                    "m.id"
                )} || ' of ' || m.name
                FROM {dialect.Table("checkpoint")} c
                JOIN {dialect.Table("metadata")} m ON m.id = c.metadata_id
                WHERE m.train_state = {dialect.State(TrainState.Completed)}
                ORDER BY c.id
                """,
                (id, what) =>
                    new TraxInvariantViolation(
                        CheckpointOfCompletedRun,
                        dialect.Table("checkpoint"),
                        id,
                        $"checkpoint {id} at {what} is kept, but that run completed"
                    ),
                cancellationToken
            )
        );

        // Not a foreign key, like replay_decisions_of, so nothing in the database holds it: the
        // metadata cleanup keeps a run while a queued entry or a run that stays resumes it, and
        // deletes a resumed run with its source.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("w.id")}, {dialect.Text("w.resume_from")}
                FROM {dialect.Table("work_queue")} w
                WHERE w.resume_from IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM {dialect.Table(
                    "metadata"
                )} m WHERE m.id = w.resume_from)
                ORDER BY w.id
                """,
                (id, source) =>
                    new TraxInvariantViolation(
                        ResumeFromMissingRun,
                        dialect.Table("work_queue"),
                        id,
                        $"queue entry {id} resumes run {source}, which does not exist"
                    ),
                cancellationToken
            )
        );

        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("r.id")}, {dialect.Text("r.resume_from")}
                FROM {dialect.Table("metadata")} r
                WHERE r.resume_from IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM {dialect.Table(
                    "metadata"
                )} m WHERE m.id = r.resume_from)
                ORDER BY r.id
                """,
                (id, source) =>
                    new TraxInvariantViolation(
                        ResumeFromMissingRun,
                        dialect.Table("metadata"),
                        id,
                        $"run {id} resumes run {source}, which does not exist"
                    ),
                cancellationToken
            )
        );

        // Entering an invoking state writes the token and the queue entry that carries the
        // instance as its invoker in one transaction, and the dispatcher copies the invoker onto
        // the run. A token with neither was set without its run, or outlived it.
        violations.AddRange(
            await ReadAsync(
                connection,
                $"""
                SELECT {dialect.Text("s.row_id")}, s.machine || ' ' || {dialect.Text(
                    "s.id"
                )} || ' in ' || s.state || ' holds ' || s.invoke_token
                FROM {dialect.Table("snapshot_draft")} s
                WHERE s.invoke_token IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM {dialect.Table("work_queue")} w
                      WHERE w.external_id = s.invoke_token
                        AND w.invoking_machine = s.machine
                        AND w.invoking_instance_id = s.id)
                  AND NOT EXISTS (
                      SELECT 1 FROM {dialect.Table("metadata")} m
                      WHERE trim(m.external_id) = s.invoke_token
                        AND m.invoking_machine = s.machine
                        AND m.invoking_instance_id = s.id)
                ORDER BY s.row_id
                """,
                (id, what) =>
                    new TraxInvariantViolation(
                        InvokeTokenWithoutRun,
                        dialect.Table("snapshot_draft"),
                        id,
                        $"{what}, which names no run this instance queued"
                    ),
                cancellationToken
            )
        );

        if (invoking.Count > 0)
            violations.AddRange(
                await FindInvokingStateViolationsAsync(
                    connection,
                    dialect,
                    invoking,
                    cancellationToken
                )
            );

        return violations;
    }

    // The token is set as the state is entered and cleared as it is left, in the write that moves
    // the instance, so the two always agree. The one exception is an instance stranded in its
    // invoking state, which the same write that clears its token marks. The rows are read and
    // compared here rather than in SQL, so both dialects run the one comparison.
    private static async Task<List<TraxInvariantViolation>> FindInvokingStateViolationsAsync(
        DbConnection connection,
        Dialect dialect,
        IReadOnlyCollection<InvokingState> invoking,
        CancellationToken cancellationToken
    )
    {
        var machines = invoking.Select(s => s.Machine).ToHashSet();
        var table = dialect.Table("snapshot_draft");

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {dialect.Text("s.row_id")}, s.machine, {dialect.Text(
                "s.id"
            )}, s.state, s.invoke_token, s.invoke_stranded_state
            FROM {table} s
            ORDER BY s.row_id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var missing = new List<TraxInvariantViolation>();
        var outside = new List<TraxInvariantViolation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var machine = reader.GetString(1);
            if (!machines.Contains(machine))
                continue;

            var rowId = reader.GetString(0);
            var id = reader.GetString(2);
            var state = reader.GetString(3);
            var token = reader.IsDBNull(4) ? null : reader.GetString(4);
            var stranded = reader.IsDBNull(5) ? null : reader.GetString(5);
            var invokes = invoking.Contains(new InvokingState(machine, state));

            if (invokes && token is null && stranded != state)
                missing.Add(
                    new TraxInvariantViolation(
                        InvokingStateWithoutToken,
                        table,
                        rowId,
                        $"{machine} {id} is in {state}, which invokes a train, and holds no invoke token"
                    )
                );
            else if (!invokes && token is not null)
                outside.Add(
                    new TraxInvariantViolation(
                        InvokeTokenOutsideInvokingState,
                        table,
                        rowId,
                        $"{machine} {id} is in {state} and holds {token}, but that state invokes nothing"
                    )
                );
        }

        return [.. missing, .. outside];
    }

    /// <summary>
    /// Throws when the database at <paramref name="connectionString"/> has any violation, listing
    /// every one with its table and id rather than stopping at the first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database is not consistent.</exception>
    public static Task AssertConsistentAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    ) => AssertConsistentAsync(connectionString, [], cancellationToken);

    /// <summary>
    /// Throws when the database at <paramref name="connectionString"/> has any violation, checking
    /// the instances of the machines in <paramref name="invokingStates"/> against those states too.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database is not consistent.</exception>
    public static async Task AssertConsistentAsync(
        string connectionString,
        IEnumerable<InvokingState> invokingStates,
        CancellationToken cancellationToken = default
    )
    {
        var violations = await FindViolationsAsync(
            connectionString,
            invokingStates,
            cancellationToken
        );
        if (violations.Count > 0)
            throw new InvalidOperationException(Describe(violations));
    }

    /// <summary>
    /// The failure message for <paramref name="violations"/>: one line each, naming the invariant,
    /// the table and the id.
    /// </summary>
    public static string Describe(IReadOnlyCollection<TraxInvariantViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        return $"The Trax database is inconsistent after every host stopped ({violations.Count} "
            + "violation(s)). If the test leaves this state on purpose, mark it "
            + "[LeavesStuckRuns(\"why\")]:\n  "
            + string.Join("\n  ", violations.Select(v => v.ToString()));
    }

    /// <summary>
    /// True when the test <paramref name="methodName"/> on <paramref name="fixture"/>, or the
    /// fixture itself or a base class of it, carries <see cref="LeavesStuckRunsAttribute"/>.
    /// </summary>
    /// <param name="fixture">The test fixture's runtime type.</param>
    /// <param name="methodName">The test method's name, or null to ask about the fixture alone.</param>
    public static bool IsExempt(Type fixture, string? methodName)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (fixture.GetCustomAttribute<LeavesStuckRunsAttribute>(inherit: true) is not null)
            return true;

        return methodName is not null
            && fixture
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == methodName)
                .Any(m =>
                    m.GetCustomAttribute<LeavesStuckRunsAttribute>(inherit: true) is not null
                );
    }

    private static async Task<List<TraxInvariantViolation>> ReadAsync(
        DbConnection connection,
        string sql,
        Func<string, string, TraxInvariantViolation> violation,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var found = new List<TraxInvariantViolation>();
        while (await reader.ReadAsync(cancellationToken))
            found.Add(violation(reader.GetString(0), reader.GetString(1)));

        return found;
    }

    /// <summary>
    /// Where the two stores' SQL differs: Postgres keeps the tables in the <c>trax</c> schema and
    /// the enums as their snake_case names; SQLite has no schemas and stores each enum as its
    /// number (its migration 014).
    /// </summary>
    private sealed class Dialect(bool postgres)
    {
        public static Dialect Of(DbConnection connection) =>
            connection switch
            {
                NpgsqlConnection => new Dialect(postgres: true),
                _ when connection.GetType().FullName == "Microsoft.Data.Sqlite.SqliteConnection" =>
                    new Dialect(postgres: false),
                _ => throw new NotSupportedException(
                    $"TraxInvariants reads Postgres and SQLite, not {connection.GetType().FullName}."
                ),
            };

        public string Table(string name) => postgres ? $"trax.{name}" : name;

        public string Text(string column) =>
            postgres ? $"{column}::text" : $"CAST({column} AS TEXT)";

        public string State(TrainState state) =>
            postgres
                ? $"'{SnakeCase(state.ToString())}'"
                : ((int)state).ToString(CultureInfo.InvariantCulture);

        public string Status(WorkQueueStatus status) =>
            postgres
                ? $"'{SnakeCase(status.ToString())}'"
                : ((int)status).ToString(CultureInfo.InvariantCulture);

        private static string SnakeCase(string name) =>
            string.Concat(
                name.Select(
                    (c, i) =>
                        i > 0 && char.IsUpper(c)
                            ? "_" + char.ToLowerInvariant(c)
                            : char.ToLowerInvariant(c).ToString()
                )
            );
    }
}

/// <summary>A state of a machine that invokes a train, as <see cref="TraxInvariants"/> checks it.</summary>
/// <param name="Machine">The machine's name, as stored in <c>trax.snapshot_draft.machine</c>.</param>
/// <param name="State">The state's name, as stored in <c>trax.snapshot_draft.state</c>.</param>
public sealed record InvokingState(string Machine, string State);

/// <summary>One row that breaks one of <see cref="TraxInvariants"/>' checks.</summary>
/// <param name="Invariant">Which check, one of the <see cref="TraxInvariants"/> constants.</param>
/// <param name="Table">The table the row is in, schema-qualified.</param>
/// <param name="Id">The row's id: the run, queue entry, checkpoint or snapshot row id, or the effect key.</param>
/// <param name="Detail">What is wrong with the row, in a sentence.</param>
public sealed record TraxInvariantViolation(
    string Invariant,
    string Table,
    string Id,
    string Detail
)
{
    /// <inheritdoc />
    public override string ToString() => $"[{Invariant}] {Table} {Id}: {Detail}";
}

/// <summary>
/// Marks a test, or a whole fixture, that leaves a Trax database inconsistent on purpose (a run
/// stuck in progress, an effect claim abandoned in flight), so the fixture's teardown skips
/// <see cref="TraxInvariants"/> for it.
/// </summary>
/// <param name="reason">Why the test leaves that state; it is read by the next person to wonder.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class LeavesStuckRunsAttribute(string reason) : Attribute
{
    /// <summary>Why the test leaves the database inconsistent.</summary>
    public string Reason { get; } = reason;
}
