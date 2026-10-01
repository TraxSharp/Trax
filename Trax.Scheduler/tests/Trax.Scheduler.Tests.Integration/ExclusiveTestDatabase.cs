using Npgsql;

namespace Trax.Scheduler.Tests.Integration;

/// <summary>
/// Holds a Postgres advisory lock on the test database for the whole run of this assembly, so a
/// second run against the same server waits for this one to finish instead of interleaving with it.
/// </summary>
/// <remarks>
/// <para>Every test here shares <c>trax_scheduler_tests</c> and starts by deleting every manifest,
/// work queue entry and metadata row in it (<see cref="Fixtures.TestSetup.CleanupDatabase"/>). The
/// tests in one run are sequential, so within a run that is safe. Two runs are not: a second
/// process, such as another worktree's <c>dotnet test</c> pointed at the same server, wipes the
/// tables between one test's scheduling and its assertions. That is how
/// <c>SchedulerPollingCycleTests.CancelAsync_CancelsPendingWorkAndUpdatesMetadata</c> failed in 17 ms
/// with "No manifest found with ExternalId 'to-cancel'", and how the rest of the suite fails with
/// foreign-key and concurrency errors under the same conditions.</para>
///
/// <para>The lock is session-level and held on a dedicated, unpooled connection, so the per-test
/// pool clears do not release it and a run that dies releases it with its connection. The wait is
/// bounded by <see cref="LockWait"/>, well past a full run, and a run that gives up says why rather
/// than failing its tests one by one. When the server cannot be reached the fixture holds nothing:
/// the tests that need the database report that themselves, and those that do not still run.</para>
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
                    + $"{LockWait.TotalMinutes} minutes. The suite deletes every row in it before each "
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
