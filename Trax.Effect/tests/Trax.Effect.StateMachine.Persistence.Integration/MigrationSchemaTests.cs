using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgresMigrator = Trax.Effect.Data.Postgres.Utils.DatabaseMigrator;
using SqliteMigrator = Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The other integration tests build the two tables with <c>EnsureCreated</c>. These build them with the
/// SHIPPED migrations (Postgres <c>040_state_machine_snapshots.sql</c>, SQLite
/// <c>006_state_machine_snapshots.sql</c>) and then round-trip through the real stores. A column added to
/// <c>SnapshotRecord</c>/<c>EffectClaim</c> without updating the migration fails here, because the store's
/// query hits a column the migration never created. This is the DDL-vs-EF-model drift guard, and it also
/// proves the two providers auto-apply their tables (no EnsureCreated, no manual DDL).
/// </summary>
public class MigrationSchemaTests
{
    private const string Maintenance =
        "Host=localhost;Port=5432;Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";

    private static Snapshot Sample() =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject
            {
                ["paidWith"] = "quarter",
                ["tags"] = new JsonArray("a", "b"),
            },
        };

    [Test]
    public async Task Postgres_migration_040_creates_the_tables_the_stores_query()
    {
        const string db = "trax_statemachine_migration_it";
        var conn =
            $"Host=localhost;Port=5432;Username=trax;Password=trax123;Database={db};Include Error Detail=true";

        await CreatePostgresDatabase(db);
        try
        {
            // Applies 001..040 to a fresh database: 040 creates trax.snapshot_draft + trax.effect_claim.
            await PostgresMigrator.Migrate(conn);

            await AssertStoresRoundTrip(() =>
                new SnapshotDbContext(
                    new DbContextOptionsBuilder<SnapshotDbContext>().UseNpgsql(conn).Options
                )
            );
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DropPostgresDatabase(db);
        }
    }

    [Test]
    public async Task Sqlite_migration_006_creates_the_tables_the_stores_query()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sm_migration_{Guid.NewGuid():N}.db");
        var conn = $"Data Source={file}";

        try
        {
            // DbUp creates the file and applies 001..006: 006 creates snapshot_draft + effect_claim
            // (unqualified, TEXT columns). SnapshotDbContext strips the "trax" schema on SQLite so the
            // stores query exactly these tables.
            await SqliteMigrator.Migrate(conn);

            await AssertStoresRoundTrip(() =>
                new SnapshotDbContext(
                    new DbContextOptionsBuilder<SnapshotDbContext>().UseSqlite(conn).Options
                )
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(file);
        }
    }

    /// <summary>
    /// Exercises every column of both tables through the stores: the snapshot's jsonb/text context, the
    /// concurrency-token CAS on Update, the request-id replay column, and the full effect-claim lifecycle
    /// (claim -> in-flight -> fenced complete -> receipt). A fresh context per call hits the database, not
    /// the EF identity map.
    /// </summary>
    private static async Task AssertStoresRoundTrip(Func<SnapshotDbContext> ctx)
    {
        const string userKey = "mig-user";
        var id = Guid.NewGuid();

        // snapshot_draft: insert, read back (context survives), then a token-guarded update + replay marker.
        (await With(ctx, c => new EfSnapshotStore(c).Upsert(userKey, id, Sample())))
            .Should()
            .BeTrue();

        var stored = await With(ctx, c => new EfSnapshotStore(c).Get(userKey, id));
        stored.Should().NotBeNull();
        stored!.Json.Should().Contain("quarter");

        var advanced = Sample() with { State = "Locked", Context = new JsonObject() };
        (
            await With(
                ctx,
                c => new EfSnapshotStore(c).Update(userKey, id, advanced, stored.Token, "req-1")
            )
        )
            .Should()
            .BeTrue();

        var after = await With(ctx, c => new EfSnapshotStore(c).Get(userKey, id));
        after.Should().NotBeNull();
        after!.Json.Should().Contain("\"state\":\"Locked\"");
        after.LastRequestId.Should().Be("req-1");

        // effect_claim: claim, confirm in-flight (no receipt), fenced complete, receipt readable back.
        var key = $"charge:{id}";
        var claim = await With(
            ctx,
            c => new EfEffectClaimStore(c).TryClaim(key, TimeSpan.FromMinutes(5))
        );
        claim.Should().BeOfType<ClaimResult.Won>();
        var owner = ((ClaimResult.Won)claim).OwnerToken;

        (await With(ctx, c => new EfEffectClaimStore(c).GetReceipt(key))).Should().BeNull();
        (await With(ctx, c => new EfEffectClaimStore(c).Complete(key, owner, "rcpt-1")))
            .Should()
            .BeTrue();
        (await With(ctx, c => new EfEffectClaimStore(c).GetReceipt(key))).Should().Be("rcpt-1");
    }

    private static async Task<T> With<T>(
        Func<SnapshotDbContext> ctx,
        Func<SnapshotDbContext, Task<T>> op
    )
    {
        await using var context = ctx();
        return await op(context);
    }

    private static async Task CreatePostgresDatabase(string database)
    {
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await Exec(admin, $"CREATE DATABASE {database}");
    }

    private static async Task DropPostgresDatabase(string database)
    {
        await using var admin = new NpgsqlConnection(Maintenance);
        await admin.OpenAsync();
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (IOException)
        {
            // Best effort — a lingering pool handle can hold the temp file; the OS reaps it later.
        }
    }
}
