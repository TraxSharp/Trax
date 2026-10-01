using Npgsql;

namespace Trax.Scheduler.Tests.Stress;

/// <summary>
/// Holds a Postgres advisory lock on the stress database for the whole run of this assembly, so a
/// second run against the same server waits for this one to finish instead of interleaving with it.
/// </summary>
/// <remarks>
/// <para>The same lock the integration suite takes on its own database, for the same reason: every
/// fixture here shares <c>trax_scheduler_stress</c> and starts by deleting every row in it
/// (<see cref="Fixtures.TestSetup"/>), so a second process running the stress suite against the
/// same server, such as another worktree's, wipes the tables under this one. Advisory locks are
/// per database, so this one and the integration suite's do not wait on each other.</para>
///
/// <para>The lock is session-level and held on a dedicated, unpooled connection, so the pool
/// clears do not release it and a run that dies releases it with its connection. The wait is
/// bounded by <see cref="LockWait"/>, and a run that gives up says why. When the server cannot be
/// reached the fixture holds nothing: the tests that need the database report that themselves.
/// Every fixture here is <c>[Explicit]</c>, so in an ordinary run nothing below this takes the
/// database at all.</para>
/// </remarks>
[SetUpFixture]
public sealed class ExclusiveTestDatabase
{
    /// <summary>An arbitrary constant naming this suite's lock; any process taking it on the same database waits.</summary>
    private const long LockKey = 0x5472_6178_5363_6864; // "TraxSchd"

    private static readonly TimeSpan LockWait = TimeSpan.FromMinutes(10);

    private NpgsqlConnection? _connection;

    [OneTimeSetUp]
    public async Task AcquireAsync()
    {
        var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(Fixtures.TestPostgres.ConnectionString)
            {
                Pooling = false,
            }.ConnectionString
        );

        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException)
        {
            await connection.DisposeAsync();
            return;
        }

        await using (var setTimeout = connection.CreateCommand())
        {
            setTimeout.CommandText = $"SET lock_timeout = '{(int)LockWait.TotalMilliseconds}ms'";
            await setTimeout.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", LockKey);
        // The server-side lock_timeout above bounds the wait; the client must not give up first.
        command.CommandTimeout = (int)LockWait.TotalSeconds + 30;

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                $"Another test run has held the {connection.Database} database for over "
                    + $"{LockWait.TotalMinutes} minutes. The stress suite deletes every row in it before each "
                    + "test, so two runs against one server cannot share it: wait for the other run, "
                    + "or point this one at its own server with TRAX_TEST_PG_PORT.",
                ex
            );
        }

        _connection = connection;
    }

    [OneTimeTearDown]
    public async Task ReleaseAsync()
    {
        if (_connection is null)
            return;

        // Closing the session releases the lock.
        await _connection.DisposeAsync();
        _connection = null;
    }
}
