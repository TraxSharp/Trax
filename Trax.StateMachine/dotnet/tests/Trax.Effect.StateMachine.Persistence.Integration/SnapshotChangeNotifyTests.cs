using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every committed write of a state-machine row on Postgres sends one notice on the snapshot change channel,
/// carrying the row's <c>row_id</c> and nothing else, whichever path wrote it: the draft service, the effect
/// runner, the system path, the outcome store calls, a host's direct store call, or a custom store's raw SQL. A
/// rolled-back write sends nothing. Heard here through a raw <c>LISTEN</c>, as another host hears it.
/// </summary>
[Property("adr", Adr)]
public class SnapshotChangeNotifyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Because =
        $"every committed write of a machine row sends one notice carrying its row_id. See {Adr}";

    private const string User = "notify-user";

    private InstanceHost _host = null!;
    private SnapshotNotices _notices = null!;

    [SetUp]
    public async Task SetUp()
    {
        _host = InstanceHost.Create(StoreProvider.Postgres, launcher: new RecordingLauncher());
        _notices = await SnapshotNotices.Listen();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _notices.DisposeAsync();
        _host.Dispose();
    }

    private static Snapshot Locked =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };

    private static Snapshot Unlocked =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = "quarter" },
        };

    private static JsonObject Quarter => new() { ["coin"] = "quarter" };

    private async Task<string> RowId(Guid id) =>
        (await _host.Rows(id)).Single().RowId.ToString(CultureInfo.InvariantCulture);

    private async Task<Guid> SavedTurnstile()
    {
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        (await _host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private async Task<Guid> StoredTurnstile()
    {
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        (await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Insert(User, id, Locked))
            .Should()
            .BeTrue();
        return id;
    }

    // A draft of a machine whose autosave is refused (one with an effect or an invoke), written as host code writes
    // one: through the store.
    private async Task<Guid> Seeded(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        (
            await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Insert(
                    User,
                    id,
                    new Snapshot
                    {
                        Machine = node["machine"]!.GetValue<string>(),
                        Version = node["version"]!.GetValue<int>(),
                        State = node["state"]!.GetValue<string>(),
                        Context = node["context"]!.AsObject().DeepClone().AsObject(),
                    }
                )
        ).Should().BeTrue();
        return id;
    }

    private async Task ShouldNotifyOnce(Guid id)
    {
        var rowId = await RowId(id);
        (await _notices.Collect()).Should().ContainSingle(p => p == rowId, Because);
    }

    private static async Task Exec(NpgsqlConnection connection, string sql, long rowId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@row", rowId);
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task Save_notifies_once_with_its_row_id()
    {
        await _notices.Drain();

        var id = await SavedTurnstile();

        await ShouldNotifyOnce(id);
    }

    [Test]
    public async Task Advance_notifies_once()
    {
        var id = await SavedTurnstile();
        await _notices.Drain();

        using (var scope = _host.Scope())
            (await _host.Service(scope, "turnstile").Advance(User, id, "Coin", Quarter))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        await ShouldNotifyOnce(id);
    }

    [Test]
    public async Task Send_after_effect_notifies_once_for_the_mark_and_once_for_the_record()
    {
        var id = await Seeded(OrderMachine.ReviewSnapshot(1, 2));
        await _notices.Drain();

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner("order")!
                    .Run(User, id, "send-1")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Placed");

        var rowId = await RowId(id);
        (await _notices.Collect())
            .Where(p => p == rowId)
            .Should()
            .HaveCount(
                2,
                $"the send marks the row, then records the effect: two writes. {Because}"
            );
    }

    [Test]
    public async Task System_start_and_advance_notify_once()
    {
        var key = MachineKey.Of("notify", Guid.NewGuid().ToString());
        await _notices.Drain();

        var instance = await _host.Start<SystemTurnstileMachine>(key);
        await ShouldNotifyOnce(instance.Id);

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemTurnstileMachine>(key, "Coin", Quarter)
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        await ShouldNotifyOnce(instance.Id);
    }

    [Test]
    public async Task Start_and_discard_notify_once()
    {
        await _notices.Drain();
        var actor = new Actor.Client(User);

        Guid id;
        using (var scope = _host.Scope())
            id = (
                await _host
                    .Service(scope, StartableMachine.Id)
                    .Start(actor, new JsonObject { ["title"] = "milk", ["copies"] = 1 }, "start-1")
            )
                .Should()
                .BeOfType<StartOutcome.Started>()
                .Which.Id;
        var rowId = await RowId(id);
        (await _notices.Collect()).Should().ContainSingle(p => p == rowId, Because);

        using (var scope = _host.Scope())
            (await _host.Service(scope, StartableMachine.Id).Discard(actor, id, "discard-1"))
                .Should()
                .BeOfType<DiscardOutcome.Discarded>();
        (await _notices.Collect()).Should().ContainSingle(p => p == rowId, Because);
    }

    [Test]
    public async Task Strand_notifies_once()
    {
        var id = await Seeded(BuildMachine.Json("Draft", new JsonObject { ["source"] = "repo" }));
        using (var scope = _host.Scope())
            (await _host.Service(scope, BuildMachine.Id).Advance(User, id, "Build"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        var row = (await _host.Rows(id)).Single();
        await _notices.Drain();

        using (var scope = _host.Scope())
            (
                await _host
                    .Instances(scope)
                    .StrandByInvokeToken(
                        row.InvokeToken!,
                        new Snapshot
                        {
                            Machine = BuildMachine.Id,
                            Version = 1,
                            State = "Building",
                            Context = new JsonObject { ["source"] = "repo" },
                        },
                        row.ConcurrencyToken
                    )
            ).Should().BeTrue();

        await ShouldNotifyOnce(id);
    }

    [Test]
    public async Task Direct_ISnapshotStore_calls_notify_once()
    {
        await _notices.Drain();

        var id = await StoredTurnstile();
        await ShouldNotifyOnce(id);

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(User, id, Unlocked)
            )
                .Should()
                .BeTrue();
        await ShouldNotifyOnce(id);

        using (var scope = _host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var token = (await store.Get(User, id))!.Token;
            (await store.Update(User, id, Locked, token)).Should().BeTrue();
        }
        await ShouldNotifyOnce(id);

        using (var scope = _host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var token = (await store.Get(User, id))!.Token;
            (
                await store.UpdateWithRequest(
                    User,
                    id,
                    Unlocked,
                    token,
                    new AppliedRequest("req-1", "Coin", "Locked")
                )
            )
                .Should()
                .BeTrue();
        }
        await ShouldNotifyOnce(id);
    }

    [Test]
    public async Task A_custom_store_writing_by_raw_sql_is_covered()
    {
        var id = await StoredTurnstile();
        var rowId = long.Parse(await RowId(id), CultureInfo.InvariantCulture);
        await _notices.Drain();

        await using (var connection = new NpgsqlConnection(PostgresSetup.ConnectionString))
        {
            await connection.OpenAsync();
            await Exec(
                connection,
                "UPDATE trax.snapshot_draft SET state = 'Unlocked', "
                    + "context = '{\"paidWith\":\"dollar\"}'::jsonb, concurrency_token = gen_random_uuid() "
                    + "WHERE row_id = @row",
                rowId
            );
        }

        await ShouldNotifyOnce(id);
    }

    [Test]
    public async Task Delete_and_DeleteHolding_notify_with_the_row_id()
    {
        var deleted = await StoredTurnstile();
        var deletedRow = await RowId(deleted);
        var held = await StoredTurnstile();
        var heldRow = await RowId(held);
        await _notices.Drain();

        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Delete(User, "turnstile", deleted);
        (await _notices.Collect()).Should().ContainSingle(p => p == deletedRow, Because);

        using (var scope = _host.Scope())
            (
                await _host
                    .Instances(scope)
                    .DeleteHolding(DraftOwner.User(User), "turnstile", held, invokeToken: null)
            )
                .Should()
                .BeTrue();
        (await _notices.Collect()).Should().ContainSingle(p => p == heldRow, Because);
    }

    [Test]
    public async Task A_sweeper_bulk_delete_notifies_once_per_row_and_the_listener_keeps_up()
    {
        const int Rows = 200;
        var ids = new List<Guid>();
        for (var i = 0; i < Rows; i++)
            ids.Add(await StoredTurnstile());
        List<string> rowIds;
        using (var scope = _host.Scope())
            rowIds = await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .Select(d => d.RowId.ToString(CultureInfo.InvariantCulture))
                .ToListAsync();
        rowIds.Should().HaveCount(Rows);
        await _notices.Drain();

        // One statement deleting many rows, as a sweep deletes the drafts it has expired.
        await using (var connection = new NpgsqlConnection(PostgresSetup.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM trax.snapshot_draft WHERE id = ANY(@ids)";
            command.Parameters.AddWithValue("@ids", ids.ToArray());
            (await command.ExecuteNonQueryAsync()).Should().Be(Rows);
        }

        var heard = (await _notices.Collect()).Where(rowIds.Contains).ToList();
        heard.Should().BeEquivalentTo(rowIds, Because);
    }

    [Test]
    public async Task A_rolled_back_write_sends_nothing()
    {
        var id = await StoredTurnstile();
        var rowId = await RowId(id);
        var row = long.Parse(rowId, CultureInfo.InvariantCulture);
        await _notices.Drain();

        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using (var rolledBack = await connection.BeginTransactionAsync())
        {
            await Exec(
                connection,
                "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid() WHERE row_id = @row",
                row
            );
            await rolledBack.RollbackAsync();
        }
        await Exec(
            connection,
            "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid() WHERE row_id = @row",
            row
        );

        (await _notices.Collect())
            .Should()
            .ContainSingle(
                p => p == rowId,
                $"the committed write notifies once and the rolled-back one not at all. See {Adr}"
            );
    }

    [Test]
    public async Task Two_writes_to_one_row_in_one_transaction_fold_into_one_notice()
    {
        var id = await StoredTurnstile();
        var rowId = await RowId(id);
        var row = long.Parse(rowId, CultureInfo.InvariantCulture);
        await _notices.Drain();

        await using (var connection = new NpgsqlConnection(PostgresSetup.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await Exec(
                connection,
                "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid() WHERE row_id = @row",
                row
            );
            await Exec(
                connection,
                "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid() WHERE row_id = @row",
                row
            );
            await transaction.CommitAsync();
        }

        (await _notices.Collect())
            .Should()
            .ContainSingle(
                p => p == rowId,
                $"Postgres folds identical payloads in one transaction, and the payload is the row_id only. See {Adr}"
            );
    }

    [Test]
    public async Task The_payload_carries_only_the_row_id_even_for_the_largest_context()
    {
        var id = await StoredTurnstile();
        var rowId = await RowId(id);
        await _notices.Drain();

        var large = new Snapshot
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = new string('q', 64 * 1024) },
        };
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(User, id, large)
            )
                .Should()
                .BeTrue();

        var heard = await _notices.Collect();
        heard.Should().Contain(rowId, Because);
        heard
            .Should()
            .AllSatisfy(p =>
                p.Should()
                    .MatchRegex(
                        @"^\d+$",
                        $"the notice is a hint: the row_id and nothing read from the row. See {Adr}"
                    )
            );
    }

    [Test]
    public async Task Delete_then_recreate_under_one_id_is_a_new_row_id_incarnation()
    {
        await _notices.Drain();
        var id = await StoredTurnstile();
        var first = await RowId(id);
        using (var scope = _host.Scope())
            await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, id);
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Insert(User, id, Locked)
            )
                .Should()
                .BeTrue();
        var second = await RowId(id);

        second.Should().NotBe(first, "a re-created row is a new incarnation");
        var heard = await _notices.Collect();
        heard
            .Where(p => p == first)
            .Should()
            .HaveCount(2, $"the insert and the delete of the first incarnation. See {Adr}");
        heard.Should().ContainSingle(p => p == second, Because);

        var revision = await RawSql.Query(
            _host,
            "SELECT revision FROM trax.snapshot_draft WHERE row_id = @row",
            ("@row", long.Parse(second, CultureInfo.InvariantCulture))
        );
        Convert
            .ToInt64(revision.Single()[0], CultureInfo.InvariantCulture)
            .Should()
            .Be(1, $"the revision restarts with the incarnation. See {Adr}");
    }

    /// <summary>The shipped Postgres migrations that notify the snapshot change channel, by resource name.</summary>
    private static List<(string Name, string Sql)> NotifyingMigrations()
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        return assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return (name, reader.ReadToEnd());
            })
            .Where(m => m.Item2.Contains(SnapshotNotices.Channel, StringComparison.Ordinal))
            .ToList();
    }

    [Test]
    public void The_listener_channel_is_the_one_the_migration_notifies()
    {
        var migrations = NotifyingMigrations();

        migrations
            .Should()
            .ContainSingle(
                $"one migration names the channel ISnapshotChangeListener listens on. See {Adr}"
            );
        var sql = migrations[0].Sql;
        sql.Should().Contain($"pg_notify('{ISnapshotChangeListener.Channel}'");
        sql.Should().Contain("trax.snapshot_draft");
    }

    [Test]
    public async Task The_trigger_migration_reruns_over_its_own_result()
    {
        var migrations = NotifyingMigrations();
        migrations.Should().ContainSingle($"one migration adds the notify trigger. See {Adr}");

        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        var before = await NotifyTriggers(connection);
        await using (var rerun = connection.CreateCommand())
        {
            rerun.CommandText = migrations[0].Sql;
            await rerun.ExecuteNonQueryAsync();
        }

        before.Should().NotBeEmpty("the migration installed the trigger");
        (await NotifyTriggers(connection))
            .Should()
            .Equal(before, $"running the migration again changes nothing. See {Adr}");
    }

    // The triggers on the draft table whose function notifies the snapshot change channel, by name.
    private static async Task<List<string>> NotifyTriggers(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT t.tgname FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid "
            + "WHERE t.tgrelid = 'trax.snapshot_draft'::regclass AND NOT t.tgisinternal "
            + "AND p.prosrc LIKE '%' || @channel || '%' ORDER BY t.tgname";
        command.Parameters.AddWithValue("@channel", SnapshotNotices.Channel);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }
}
