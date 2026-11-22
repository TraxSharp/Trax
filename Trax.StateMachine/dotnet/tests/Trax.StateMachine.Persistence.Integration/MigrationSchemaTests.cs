using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using PostgresMigrator = Trax.Effect.Data.Postgres.Utils.DatabaseMigrator;
using SqliteMigrator = Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// The other integration tests build the two tables with <c>EnsureCreated</c>. These build them with the
/// SHIPPED migrations (Postgres <c>040_state_machine_snapshots.sql</c> through <c>071_snapshot_draft_operator_listing.sql</c>,
/// SQLite <c>006_state_machine_snapshots.sql</c> through <c>033_snapshot_draft_operator_listing.sql</c>) and then round-trip through the real stores. A column added to
/// <c>SnapshotDraft</c>/<c>EffectClaim</c> without updating the migration fails here, because the store's
/// query hits a column the migration never created. This is the DDL-vs-EF-model drift guard, and it also
/// proves the two providers auto-apply their tables (no EnsureCreated, no manual DDL).
///
/// <para>Enforces <c>docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md")]
public class MigrationSchemaTests
{
    private static readonly string Maintenance = string.Create(
        CultureInfo.InvariantCulture,
        $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres;Include Error Detail=true"
    );

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
        var conn = string.Create(
            CultureInfo.InvariantCulture,
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={db};Include Error Detail=true"
        );

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
        await using var hostDisposal = host.ConfigureAwait(false);
        var dialect = host.GetRequiredService<ISqlDialect>();
        IDataContext ctx() =>
            (IDataContext)host.GetRequiredService<IDataContextProviderFactory>().Create();

        const string userKey = "mig-user";
        var id = Guid.NewGuid();

        var (advanced, after) = await InsertAndAdvanceUserDraft(ctx, dialect, userKey, id)
            .ConfigureAwait(false);
        var scoped = await AssertRequestScope(ctx, dialect, userKey, id, after)
            .ConfigureAwait(false);
        var token = await InsertSystemRowWithToken(ctx, dialect, id).ConfigureAwait(false);
        await AssertTokenAppliedAndCreatedAt(ctx, dialect, userKey, id, token, advanced, scoped)
            .ConfigureAwait(false);
        await AssertEffectClaimLifecycle(ctx, dialect, id).ConfigureAwait(false);
        await AssertEffectClaimFingerprint(ctx, dialect, id).ConfigureAwait(false);
    }

    // snapshot_draft through the store: insert, read back, then a token-guarded update with a replay marker.
    private static async Task<(Snapshot Advanced, StoredSnapshot After)> InsertAndAdvanceUserDraft(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        string userKey,
        Guid id
    )
    {
        // snapshot_draft: insert, read back (context survives), then a token-guarded update + replay marker.
        (
            await With(ctx, c => new EfSnapshotStore(c, dialect).Upsert(userKey, id, Sample()))
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();

        var stored = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id))
            .ConfigureAwait(false);
        stored
            .Should()
            .NotBeNull(
                "the shipped migration must create every column the store queries. A model "
                    + "change without a migration fails here. See "
                    + "docs/adr/0002-framework-tables-are-migrated-domain-tables-are-bootstrapped.md."
            );
        stored.NotNull().Json.Should().Contain("quarter");

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
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();

        var after = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id))
            .ConfigureAwait(false);
        after.Should().NotBeNull();
        after.NotNull().Json.Should().Contain("\"state\":\"Locked\"");
        after.LastRequestId.Should().Be("req-1");
        return (advanced, after.NotNull());
    }

    private static async Task<StoredSnapshot> AssertRequestScope(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        string userKey,
        Guid id,
        StoredSnapshot after
    )
    {
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
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();
        var scoped = await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id))
            .ConfigureAwait(false);
        scoped.NotNull().LastRequest.Should().Be(new AppliedRequest("req-2", "Coin", "Locked"));
        return scoped.NotNull();
    }

    private static async Task<string> InsertSystemRowWithToken(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        Guid id
    )
    {
        // The owner kind and the invoke token (070 / 032): a system row beside the user's under the same id,
        // a token set on it, and the conditional update by that token.
        (
            await With(
                    ctx,
                    c =>
                        ((IMachineInstanceStore)new EfSnapshotStore(c, dialect)).Insert(
                            DraftOwner.System,
                            id,
                            Sample()
                        )
                )
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue("a system row is unique among system rows, apart from the user's");
        var system = await With(
                ctx,
                c =>
                    ((IMachineInstanceStore)new EfSnapshotStore(c, dialect)).Get(
                        DraftOwner.System,
                        "turnstile",
                        id
                    )
            )
            .ConfigureAwait(false);
        system.Should().NotBeNull();
        var token = $"run-{id:N}";
        (
            await With(
                    ctx,
                    c =>
                        ((IMachineInstanceStore)new EfSnapshotStore(c, dialect)).SetInvokeToken(
                            DraftOwner.System,
                            "turnstile",
                            id,
                            system.NotNull().Token,
                            new InvokeTokenWrite(token)
                        )
                )
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();
        return token;
    }

    private static async Task AssertTokenAppliedAndCreatedAt(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        string userKey,
        Guid id,
        string token,
        Snapshot advanced,
        StoredSnapshot scoped
    )
    {
        (
            await With(
                    ctx,
                    c =>
                        ((IMachineInstanceStore)new EfSnapshotStore(c, dialect)).ApplyByInvokeToken(
                            token,
                            advanced,
                            nextInvokeToken: null
                        )
                )
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();
        (
            await With(ctx, c => new EfSnapshotStore(c, dialect).Get(userKey, id))
                .ConfigureAwait(false)
        )
            .NotNull()
            .Json.Should()
            .Be(scoped.Json, "the user's draft under the same id is untouched");

        // created_at (071 / 033): written on insert and left alone by every update since.
        var rows = await With(
                ctx,
                c =>
                    c.SnapshotDrafts.AsNoTracking()
                        .Where(x => x.Id == id)
                        .Select(x => new { x.CreatedAt, x.UpdatedAt })
                        .ToListAsync()
            )
            .ConfigureAwait(false);
        rows.Should().HaveCount(2);
        rows.Should()
            .OnlyContain(
                x => x.CreatedAt != null && x.CreatedAt <= x.UpdatedAt,
                "the store records when it created each row, and the updates since moved only updated_at"
            );
    }

    private static async Task AssertEffectClaimLifecycle(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        Guid id
    )
    {
        // effect_claim: claim, confirm in-flight (no receipt), fenced complete, receipt readable back.
        var key = $"charge:{id}";
        var claim = await With(
                ctx,
                c => new EfEffectClaimStore(c, dialect).TryClaim(key, TimeSpan.FromMinutes(5))
            )
            .ConfigureAwait(false);
        claim.Should().BeOfType<ClaimResult.Won>();
        var owner = ((ClaimResult.Won)claim).OwnerToken;

        (
            await With(ctx, c => new EfEffectClaimStore(c, dialect).GetReceipt(key))
                .ConfigureAwait(false)
        )
            .Should()
            .BeNull();
        (
            await With(ctx, c => new EfEffectClaimStore(c, dialect).Complete(key, owner, "rcpt-1"))
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();
        (
            await With(ctx, c => new EfEffectClaimStore(c, dialect).GetReceipt(key))
                .ConfigureAwait(false)
        )
            .Should()
            .Be("rcpt-1");
    }

    private static async Task AssertEffectClaimFingerprint(
        Func<IDataContext> ctx,
        ISqlDialect dialect,
        Guid id
    )
    {
        // The content fingerprint (053 / 018): recorded with the claim, read back with its receipt.
        var bound = $"charge-bound:{id}";
        var fingerprint = SnapshotFingerprint.Of("{\"machine\":\"turnstile\"}");
        var boundClaim = await With(
                ctx,
                c =>
                    new EfEffectClaimStore(c, dialect).TryClaim(
                        bound,
                        TimeSpan.FromMinutes(5),
                        fingerprint
                    )
            )
            .ConfigureAwait(false);
        var boundOwner = boundClaim.Should().BeOfType<ClaimResult.Won>().Which.OwnerToken;
        (
            await With(
                    ctx,
                    c => new EfEffectClaimStore(c, dialect).Complete(bound, boundOwner, "rcpt-2")
                )
                .ConfigureAwait(false)
        )
            .Should()
            .BeTrue();
        (
            await With(ctx, c => new EfEffectClaimStore(c, dialect).GetCompleted(bound))
                .ConfigureAwait(false)
        )
            .Should()
            .Be(
                new CompletedEffect("rcpt-2", fingerprint),
                "the claim keeps the fingerprint of the content its effect ran on. See "
                    + "docs/adr/0002-only-the-effect-runner-reaches-a-committed-state.md."
            );
    }

    private static async Task<T> With<T>(Func<IDataContext> ctx, Func<IDataContext, Task<T>> op)
    {
        var context = ctx();
        await using var contextDisposal = context.ConfigureAwait(false);
        return await op(context).ConfigureAwait(false);
    }

    private static async Task CreatePostgresDatabase(string database)
    {
        var admin = new NpgsqlConnection(Maintenance);
        await using var adminDisposal = admin.ConfigureAwait(false);
        await admin.OpenAsync().ConfigureAwait(false);
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)").ConfigureAwait(false);
        await Exec(admin, $"CREATE DATABASE {database}").ConfigureAwait(false);
    }

    private static async Task DropPostgresDatabase(string database)
    {
        var admin = new NpgsqlConnection(Maintenance);
        await using var adminDisposal = admin.ConfigureAwait(false);
        await admin.OpenAsync().ConfigureAwait(false);
        await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)").ConfigureAwait(false);
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        await using var commandDisposal = command.ConfigureAwait(false);
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
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
