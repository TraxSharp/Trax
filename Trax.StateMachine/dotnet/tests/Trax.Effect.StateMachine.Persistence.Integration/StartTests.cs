using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using SqliteMigrator = Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <c>start</c> creates an instance from the machine's declared start input, never from a context the client wrote:
/// the server mints the id, validates the input record, and answers a retry with the same request id and input with
/// the same instance, once per owner and machine however many retries race.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class StartTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private InstanceHost _host = null!;
    private RecordingLauncher _launcher = null!;

    [SetUp]
    public void SetUp()
    {
        _launcher = new RecordingLauncher();
        _host = InstanceHost.Create(provider, launcher: _launcher);
    }

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static string NewUser() => $"u-{Guid.NewGuid():N}";

    private static JsonObject Note(string title = "groceries", int copies = 2) =>
        new() { ["title"] = title, ["copies"] = copies };

    private async Task<StartOutcome> Start(
        string machine,
        Actor actor,
        JsonObject? input,
        string requestId
    )
    {
        using var scope = _host.Scope();
        return await _host.Service(scope, machine).Start(actor, input, requestId);
    }

    private static StartOutcome.Started Started(StartOutcome outcome) =>
        outcome.Should().BeOfType<StartOutcome.Started>().Which;

    [Test]
    public async Task Start_returns_a_server_minted_id_at_revision_one()
    {
        var user = NewUser();

        var started = Started(
            await Start(StartableMachine.Id, new Actor.Client(user), Note("milk"), "s1")
        );

        started.Id.Should().NotBe(Guid.Empty, $"the server mints the id. See {Adr}");
        started.Revision.Should().Be(1);
        started.Created.Should().BeTrue();
        started.Snapshot.State.Should().Be(nameof(ListState.Drafting));
        started.Snapshot.Context["title"]!.GetValue<string>().Should().Be("milk");
        (await _host.UserRows(user, StartableMachine.Id))
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(started.Id);
    }

    private static IEnumerable<TestCaseData> InvalidStartInputs()
    {
        yield return new TestCaseData(
            new JsonObject
            {
                ["title"] = "t",
                ["copies"] = 1,
                ["artifact"] = "context only",
            },
            RefusalCode.InvalidInput
        ).SetArgDisplayNames("a context field the start input does not declare");
        yield return new TestCaseData(
            new JsonObject { ["title"] = "t", ["copies"] = "2" },
            RefusalCode.InvalidInput
        ).SetArgDisplayNames("a number sent as a string");
        yield return new TestCaseData(
            new JsonObject { ["title"] = "t" },
            RefusalCode.InvalidInput
        ).SetArgDisplayNames("a missing field");
        yield return new TestCaseData(
            new JsonObject { ["title"] = new string('x', 70 * 1024), ["copies"] = 1 },
            RefusalCode.TooLarge
        ).SetArgDisplayNames("70 KiB");
    }

    [TestCaseSource(nameof(InvalidStartInputs))]
    public async Task Start_validates_its_StartInput_record(JsonObject input, RefusalCode code)
    {
        var user = NewUser();

        (await Start(StartableMachine.Id, new Actor.Client(user), input, "s1"))
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(code, $"start validates its declared input record, never the context. See {Adr}");
        (await _host.UserRows(user, StartableMachine.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task Start_refuses_a_StartInput_reaching_a_server_written_field()
    {
        var refusal = await StartingHost.Start(_ => { }, Marked.Machine<ForgedStartMachine>());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"a client must not start an instance already holding what only a gated event writes. See {Adr}"
            )
            .Which.Message.Should()
            .Contain(ForgedStartMachine.Id)
            .And.ContainEquivalentOf("approvedBy");
    }

    [Test]
    public void Start_refuses_a_StartInput_reaching_a_field_a_policy_rule_writes()
    {
        // Read from the machine itself: a host that registers no such policy refuses it for that first.
        IMachineInternals machine = new PolicyWriteStartMachine();

        machine
            .StartProblems.Should()
            .ContainSingle(
                $"a policy gates an event as a role does, so its write is the server's alone. See {Adr}"
            )
            .Which.Should()
            .Contain(PolicyWriteStartMachine.Id)
            .And.ContainEquivalentOf("approvedBy");
    }

    [TestCase("", TestName = "{m}(empty)")]
    [TestCase("   ", TestName = "{m}(whitespace)")]
    [TestCase(null, TestName = "{m}(oversized)")]
    public async Task Start_with_an_empty_whitespace_or_oversized_requestId_is_refused(
        string? requestId
    )
    {
        var user = NewUser();

        (
            await Start(
                StartableMachine.Id,
                new Actor.Client(user),
                Note(),
                requestId ?? new string('r', 4096)
            )
        )
            .Should()
            .BeOfType<StartOutcome.Rejected>(
                $"a start without a usable request id cannot be retried safely. See {Adr}"
            );
        (await _host.UserRows(user, StartableMachine.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task Two_starts_with_one_requestId_and_one_input_return_one_instance()
    {
        var user = NewUser();

        var first = Started(await Start(StartableMachine.Id, new Actor.Client(user), Note(), "s1"));
        var retry = Started(await Start(StartableMachine.Id, new Actor.Client(user), Note(), "s1"));

        retry
            .Id.Should()
            .Be(first.Id, $"a retried start returns the instance it created. See {Adr}");
        retry.Created.Should().BeFalse();
        (await _host.UserRows(user, StartableMachine.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task A_retried_start_with_a_different_input_is_request_id_reused()
    {
        var user = NewUser();
        Started(await Start(StartableMachine.Id, new Actor.Client(user), Note("milk"), "s1"));

        (await Start(StartableMachine.Id, new Actor.Client(user), Note("eggs"), "s1"))
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(
                RefusalCode.RequestIdReused,
                $"one request id names one start, told apart by its input's fingerprint. See {Adr}"
            );
        (await _host.UserRows(user, StartableMachine.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Two_users_with_one_requestId_get_different_instances()
    {
        var alice = NewUser();
        var bob = NewUser();

        var hers = Started(await Start(StartableMachine.Id, new Actor.Client(alice), Note(), "s1"));
        var his = Started(await Start(StartableMachine.Id, new Actor.Client(bob), Note(), "s1"));

        his.Id.Should().NotBe(hers.Id, $"a request id is scoped to its owner. See {Adr}");
        using var scope = _host.Scope();
        var service = _host.Service(scope, StartableMachine.Id);
        (await service.Load(alice, his.Id)).Should().BeOfType<LoadResult.NotFound>();
        (await service.Load(bob, hers.Id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    public async Task One_requestId_on_two_machines_gives_two_instances()
    {
        var user = NewUser();

        var note = Started(await Start(StartableMachine.Id, new Actor.Client(user), Note(), "s1"));
        var twin = Started(
            await Start(StartableTwinMachine.Id, new Actor.Client(user), Note(), "s1")
        );

        note.Created.Should().BeTrue();
        twin.Created.Should()
            .BeTrue($"the request id is unique per owner kind, user and machine. See {Adr}");
        (await _host.UserRows(user, StartableMachine.Id)).Should().ContainSingle();
        (await _host.UserRows(user, StartableTwinMachine.Id)).Should().ContainSingle();
    }

    [TestCase("", TestName = "{m}(unauthenticated)")]
    [TestCase("  ", TestName = "{m}(whitespace)")]
    public async Task Start_by_an_unauthenticated_or_whitespace_principal_writes_nothing(
        string userKey
    )
    {
        (
            await Start(
                StartableMachine.Id,
                new Actor.Client(userKey),
                Note(),
                $"s-{Guid.NewGuid():N}"
            )
        )
            .Should()
            .BeOfType<StartOutcome.Rejected>($"an instance needs a real owner. See {Adr}");
        (await _host.UserRows(userKey, StartableMachine.Id)).Should().BeEmpty();
    }

    [Test]
    public async Task Start_into_an_invoking_initial_state_queues_the_run_in_its_transaction()
    {
        var user = NewUser();

        var started = Started(
            await Start(
                InvokingStartMachine.Id,
                new Actor.Client(user),
                new JsonObject { ["source"] = "repo" },
                "s1"
            )
        );

        var row = (await _host.Rows(started.Id)).Single();
        row.InvokeToken.Should()
            .NotBeNull($"starting in an invoking state queues its run. See {Adr}");
        (await _host.Entries(row.InvokeToken!)).Should().ContainSingle();
        _launcher.Launches.Should().ContainSingle();

        // The machine allows one live run per owner: a second start is refused and inserts nothing.
        (
            await Start(
                InvokingStartMachine.Id,
                new Actor.Client(user),
                new JsonObject { ["source"] = "repo" },
                "s2"
            )
        )
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(RefusalCode.InvokeLimitReached);
        (await _host.UserRows(user, InvokingStartMachine.Id)).Should().ContainSingle();
        _launcher.Launches.Should().ContainSingle();
    }

    [Test]
    public async Task A_live_run_still_counts_after_its_instance_is_gone()
    {
        // The cap counts by the run's recorded starter, so a run whose instance a draft lifetime or a discard removed
        // keeps its place until it ends
        // (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md).
        var user = NewUser();
        var started = Started(
            await Start(
                InvokingStartMachine.Id,
                new Actor.Client(user),
                new JsonObject { ["source"] = "repo" },
                "s1"
            )
        );
        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.Where(x => x.Id == started.Id)
                .ExecuteDeleteAsync();

        (
            await Start(
                InvokingStartMachine.Id,
                new Actor.Client(user),
                new JsonObject { ["source"] = "repo" },
                "s2"
            )
        )
            .Should()
            .BeOfType<StartOutcome.Rejected>(
                "the first run is still queued, though its instance is gone"
            )
            .Which.Code.Should()
            .Be(RefusalCode.InvokeLimitReached);
    }

    [Test]
    public async Task Start_on_a_system_owned_machine_is_refused()
    {
        var user = NewUser();
        using var scope = _host.Scope();
        var service = new RuledSystemTurnstileMachine().CreateService(
            scope.ServiceProvider.GetRequiredService<ISnapshotStore>(),
            scope.ServiceProvider.GetRequiredService<IEffectClaimStore>()
        );

        (await service.Start(new Actor.Client(user), null, "s1"))
            .Should()
            .BeOfType<StartOutcome.Rejected>(
                $"only code creates a system-owned instance. See {Adr}"
            );
        (await _host.UserRows(user, RuledSystemTurnstileMachine.Id)).Should().BeEmpty();
    }

    [Test]
    [CancelAfter(60_000)]
    public async Task Twenty_parallel_starts_with_one_requestId_create_one_row()
    {
        // A race on the unique index is the point, so this runs on Postgres under either fixture.
        using var host = InstanceHost.Create(StoreProvider.Postgres);
        var user = NewUser();

        var starts = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        using var scope = host.Scope();
                        return await host.Service(scope, StartableMachine.Id)
                            .Start(new Actor.Client(user), Note(), "s1");
                    })
                )
        );

        starts.Should().AllBeOfType<StartOutcome.Started>();
        starts
            .Cast<StartOutcome.Started>()
            .Select(s => s.Id)
            .Distinct()
            .Should()
            .ContainSingle($"every racing retry gets the one instance. See {Adr}");
        (await host.UserRows(user, StartableMachine.Id)).Should().ContainSingle();
    }

    [Test]
    public async Task Migration_adds_start_request_id_with_its_unique_index()
    {
        if (provider == StoreProvider.Postgres)
        {
            await using var conn = new NpgsqlConnection(PostgresSetup.ConnectionString);
            await conn.OpenAsync();
            await using var column = conn.CreateCommand();
            column.CommandText =
                "SELECT count(*) FROM information_schema.columns "
                + "WHERE table_schema = 'trax' AND table_name = 'snapshot_draft' AND column_name = 'start_request_id'";
            ((long)(await column.ExecuteScalarAsync())!)
                .Should()
                .Be(1, $"start keeps its request id on the row. See {Adr}");

            await using var index = conn.CreateCommand();
            index.CommandText =
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'trax' AND tablename = 'snapshot_draft' "
                + "AND indexdef LIKE '%start_request_id%'";
            var definitions = new List<string>();
            await using (var reader = await index.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    definitions.Add(reader.GetString(0));
            definitions
                .Should()
                .ContainSingle()
                .Which.Should()
                .StartWith("CREATE UNIQUE INDEX")
                .And.Contain("user_key")
                .And.Contain("machine");
            return;
        }

        var file = Path.Combine(Path.GetTempPath(), $"sm_start_{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={file}";
            await SqliteMigrator.Migrate(connectionString);
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();
            await using var column = conn.CreateCommand();
            column.CommandText =
                "SELECT count(*) FROM pragma_table_info('snapshot_draft') WHERE name = 'start_request_id'";
            ((long)(await column.ExecuteScalarAsync())!)
                .Should()
                .Be(1, $"start keeps its request id on the row. See {Adr}");

            await using var index = conn.CreateCommand();
            index.CommandText =
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'snapshot_draft' "
                + "AND sql LIKE '%start_request_id%'";
            ((string?)await index.ExecuteScalarAsync())
                .Should()
                .NotBeNull()
                .And.StartWith("CREATE UNIQUE INDEX");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }
}
