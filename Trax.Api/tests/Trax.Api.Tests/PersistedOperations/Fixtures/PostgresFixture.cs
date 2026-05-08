using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Extensions;

namespace Trax.Api.Tests.PersistedOperations;

/// <summary>
/// Shared fixture for persisted-operation integration tests. Builds the
/// service provider ONCE per test process so migrations run once and the
/// Npgsql connection pool stays small. Per-test cleanup happens in each
/// fixture's <c>[SetUp]</c> via <see cref="ClearAsync"/>.
/// </summary>
[SetUpFixture]
public class PostgresFixture
{
    /// <summary>
    /// Connection string used by every persisted-operation integration test.
    /// <para>
    /// Timeout is bumped from the Npgsql default (15s) to 30s so the slow
    /// service-side Postgres in CI does not flake on connection acquisition
    /// when many tests run back-to-back. Pool size capped to 16 to prevent
    /// a single test process from exhausting the CI Postgres instance.
    /// </para>
    /// </summary>
    public const string ConnectionString =
        "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123;"
        + "Timeout=30;Command Timeout=30;Maximum Pool Size=16";

    private static ServiceProvider? _services;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    [OneTimeSetUp]
    public async Task EnsureMigratedAndProviderBuiltAsync()
    {
        // Initialise the shared provider once per process. Subsequent fixture
        // OneTimeSetUps short-circuit on the static field. The semaphore
        // covers the case where NUnit decides to run multiple SetUpFixtures
        // in parallel (it does not by default, but defensive coding here is
        // cheap).
        if (_services is not null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_services is not null)
                return;
            await DatabaseMigrator.Migrate(ConnectionString);
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(ConnectionString)));
            _services = sc.BuildServiceProvider();
        }
        finally
        {
            _initLock.Release();
        }
    }

    [OneTimeTearDown]
    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
            _services = null;
        }
    }

    /// <summary>
    /// True when Postgres is reachable. Tests skip themselves when this
    /// returns false. Uses an explicit short-timeout connection string so
    /// the reachability probe doesn't hang.
    /// </summary>
    public static bool IsPostgresReachable()
    {
        try
        {
            using var conn = new NpgsqlConnection(
                "Host=localhost;Port=5432;Database=trax;Username=trax;Password=trax123;Timeout=5"
            );
            conn.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the shared service provider. Callers MUST NOT dispose it.
    /// </summary>
    public static IServiceProvider Services =>
        _services
        ?? throw new InvalidOperationException(
            "PostgresFixture.Services accessed before [OneTimeSetUp] ran. "
                + "Did the test class run outside the SetUpFixture's namespace?"
        );

    /// <summary>
    /// Compatibility shim for tests that previously built their own provider.
    /// Returns the shared instance; do not dispose.
    /// </summary>
    public static IServiceProvider BuildServiceProvider() => Services;

    /// <summary>
    /// Truncate persisted-operation tables. Call from <c>[SetUp]</c>.
    /// </summary>
    public static async Task ClearAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "truncate trax.persisted_operation, trax.persisted_operation_history;",
            conn
        );
        await cmd.ExecuteNonQueryAsync();
    }
}
