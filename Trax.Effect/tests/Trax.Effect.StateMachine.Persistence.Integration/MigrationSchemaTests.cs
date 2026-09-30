using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using PostgresMigrator = Trax.Effect.Data.Postgres.Utils.DatabaseMigrator;
using SqliteMigrator = Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The other integration tests build the two tables with <c>EnsureCreated</c>. These build them with the
/// SHIPPED migrations (Postgres <c>040_state_machine_snapshots.sql</c> and <c>048_snapshot_draft_request_scope.sql</c>,
/// SQLite <c>006_state_machine_snapshots.sql</c> and <c>013_snapshot_draft_request_scope.sql</c>) and then round-trip through the real stores. A column added to
/// <c>SnapshotDraft</c>/<c>EffectClaim</c> without updating the migration fails here, because the store's
/// query hits a column the migration never created. This is the DDL-vs-EF-model drift guard, and it also
/// proves the two providers auto-apply their tables (no EnsureCreated, no manual DDL).
///
/// <para>Enforces <c>docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md")]
public class MigrationSchemaTests
{
    private static readonly string Maintenance =
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres;Include Error Detail=true";

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
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={db};Include Error Detail=true";

        await CreatePostgresDatabase(db);
        try
        {
            // Applies 001..040 to a fresh database: 040 creates trax.snapshot_draft + trax.effect_claim.
            await PostgresMigrator.Migrate(conn);

            await AssertStoresRoundTrip(Host(effects => effects.UsePostgres(conn)));
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
            // (unqualified, TEXT columns). The SQLite data context strips the "trax" schema so the
            // stores query exactly these tables.
            await SqliteMigrator.Migrate(conn);

            await AssertStoresRoundTrip(Host(effects => effects.UseSqlite(conn)));
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
    /// <summary>The data context and dialect a host on this provider gets, with the migrations already applied.</summary>
    private static ServiceProvider Host(Func<TraxEffectBuilder, TraxEffectBuilderWithData> provider)
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax.AddEffects(effects => provider(effects)));
        return services.BuildServiceProvider();
    }

    private static async Task AssertStoresRoundTrip(ServiceProvider host)
    {
        using var _ = host;
        var dialect = host.GetRequiredService<ISqlDialect>();
        IDataContext ctx() =>
            (IDataContext)host.GetRequiredService<IDataContextProviderFactory>().Create();

        const string userKey = "mig-user";
        var id = Guid.NewGuid();

        // snapshot_draft: insert, read back (context survives), then a token-guarded update + replay marker.
        (await With(ctx, c => new EfSnapshotStore(c, dialect).Upsert(userKey, id, Sample())))
            .Should()
            .BeTrue();

        var stored = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id));
        stored
            .Should()
            .NotBeNull(
                "the shipped migration must create every column the store queries. A model "
                    + "change without a migration fails here. See "
                    + "docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md."
            );
        stored!.Json.Should().Contain("quarter");

        var advanced = Sample() with { State = "Locked", Context = new JsonObject() };
        (
            await With(
                ctx,
                c =>
                    new EfSnapshotStore(c, dialect).Update(
                        userKey,
                        id,
                        advanced,
                        stored.Token,
                        "req-1"
                    )
            )
        )
            .Should()
            .BeTrue();

        var after = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id));
        after.Should().NotBeNull();
        after!.Json.Should().Contain("\"state\":\"Locked\"");
        after.LastRequestId.Should().Be("req-1");

        // The request scope columns (048 / 013): the trigger and from-state recorded with the request id.
        (
            await With(
                ctx,
                c =>
                    new EfSnapshotStore(c, dialect).UpdateWithRequest(
                        userKey,
                        id,
                        Sample(),
                        after.Token,
                        new AppliedRequest("req-2", "Coin", "Locked")
                    )
            )
        )
            .Should()
            .BeTrue();
        var scoped = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id));
        scoped!.LastRequest.Should().Be(new AppliedRequest("req-2", "Coin", "Locked"));

        // effect_claim: claim, confirm in-flight (no receipt), fenced complete, receipt readable back.
        var key = $"charge:{id}";
        var claim = await With(
            ctx,
            c => new EfEffectClaimStore(c, dialect).TryClaim(key, TimeSpan.FromMinutes(5))
        );
        claim.Should().BeOfType<ClaimResult.Won>();
        var owner = ((ClaimResult.Won)claim).OwnerToken;

        (await With(ctx, c => new EfEffectClaimStore(c, dialect).GetReceipt(key)))
            .Should()
            .BeNull();
        (await With(ctx, c => new EfEffectClaimStore(c, dialect).Complete(key, owner, "rcpt-1")))
            .Should()
            .BeTrue();
        (await With(ctx, c => new EfEffectClaimStore(c, dialect).GetReceipt(key)))
            .Should()
            .Be("rcpt-1");
    }

    private static async Task<T> With<T>(Func<IDataContext> ctx, Func<IDataContext, Task<T>> op)
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
