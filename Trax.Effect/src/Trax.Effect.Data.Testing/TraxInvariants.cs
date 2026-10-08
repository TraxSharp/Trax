using System.Reflection;
using Npgsql;

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Checks that a Trax Postgres database is consistent once every host using it has stopped: no run
/// is still in progress, no state-machine effect is still claimed in flight, and every dispatched
/// queue entry has its run. While a host runs, each of these is a normal transient state; once they
/// have all stopped, each one is work that was started and then lost.
/// </summary>
/// <remarks>
/// <para>Call it from a test fixture's teardown, after the hosts the test started are disposed. A
/// test that leaves one of these states on purpose (a crash simulation, an abandoned claim) carries
/// <see cref="LeavesStuckRunsAttribute"/>, and the fixture skips the check for it with
/// <see cref="IsExempt"/>.</para>
/// <para>It reads the <c>trax</c> schema the shipped migrations create, and only Postgres.</para>
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
    /// Every violation in the database at <paramref name="connectionString"/>, in a stable order:
    /// by invariant, then by id.
    /// </summary>
    public static async Task<IReadOnlyList<TraxInvariantViolation>> FindViolationsAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var violations = new List<TraxInvariantViolation>();

        // A run a host had started and never finished. After every host has stopped, nothing will
        // finish it: stuck-job recovery is the only thing that would move it, and only later.
        violations.AddRange(
            await ReadAsync(
                connection,
                """
                SELECT id::text, name
                FROM trax.metadata
                WHERE train_state = 'in_progress'
                ORDER BY id
                """,
                (id, name) =>
                    new TraxInvariantViolation(
                        RunInProgress,
                        "trax.metadata",
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
                """
                SELECT effect_key, owner_token::text
                FROM trax.effect_claim
                WHERE receipt IS NULL
                ORDER BY effect_key
                """,
                (key, owner) =>
                    new TraxInvariantViolation(
                        EffectClaimInFlight,
                        "trax.effect_claim",
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
                """
                SELECT w.id::text, coalesce(w.metadata_id::text, 'null')
                FROM trax.work_queue w
                LEFT JOIN trax.metadata m ON m.id = w.metadata_id
                WHERE w.status = 'dispatched' AND m.id IS NULL
                ORDER BY w.id
                """,
                (id, run) =>
                    new TraxInvariantViolation(
                        DispatchedWithoutRun,
                        "trax.work_queue",
                        id,
                        $"queue entry {id} is dispatched but its run ({run}) does not exist"
                    ),
                cancellationToken
            )
        );

        return violations;
    }

    /// <summary>
    /// Throws when the database at <paramref name="connectionString"/> has any violation, listing
    /// every one with its table and id rather than stopping at the first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The database is not consistent.</exception>
    public static async Task AssertConsistentAsync(
        string connectionString,
        CancellationToken cancellationToken = default
    )
    {
        var violations = await FindViolationsAsync(connectionString, cancellationToken);
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
        NpgsqlConnection connection,
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
}

/// <summary>One row that breaks one of <see cref="TraxInvariants"/>' checks.</summary>
/// <param name="Invariant">Which check, one of the <see cref="TraxInvariants"/> constants.</param>
/// <param name="Table">The table the row is in, schema-qualified.</param>
/// <param name="Id">The row's id: the run or queue entry id, or the effect key.</param>
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
