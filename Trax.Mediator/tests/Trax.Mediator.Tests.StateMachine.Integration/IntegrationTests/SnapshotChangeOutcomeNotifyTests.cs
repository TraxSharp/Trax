using System.Collections.Concurrent;
using AwesomeAssertions;
using Npgsql;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// A write that enters an invoking state sends exactly one change notice on <c>trax_snapshot_changed</c>, carrying
/// the row's <c>row_id</c> and nothing else, like every other committed write to the snapshot table. The outcome
/// paths (the run's own host, through its hook, and another host's reconciler) are in
/// <c>Trax.Scheduler.Tests.Integration</c>'s test of the same name, which is where a run can be dispatched and
/// finished across hosts. Postgres only: the notice is a Postgres <c>NOTIFY</c>. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class SnapshotChangeOutcomeNotifyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Channel = "trax_snapshot_changed";

    // Every wait in this class: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InvokeHost.Create(StoreProvider.Postgres);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Advance_into_invoke_notifies_once()
    {
        const string user = "notify-user";
        var id = Guid.NewGuid();
        TestPrincipal.Become(user);
        await _host.SeedIdle(user, id, GoodMachine.MachineId);

        await using var listener = await Listener.Open();
        // A fresh LISTEN can be handed notices committed just before it; only what the act sends counts.
        _ = await listener.Drain();

        using (var scope = _host.Scope())
            (await _host.Service(scope, GoodMachine.MachineId).Advance(user, id, "Go"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        (await _host.Runs(id)).Should().ContainSingle("the advance queued its run");

        var rowId = (await _host.Row(id, GoodMachine.MachineId))!.RowId;
        var notices = await listener.Drain();
        notices
            .Should()
            .ContainSingle(
                p => p == rowId.ToString(),
                $"the advance into the invoking state is one committed write, and it sends one notice. See {Adr}"
            );
    }

    /// <summary>
    /// A session listening on the change channel. Once a query on it returns, every notice committed before that
    /// query began has reached it, so <see cref="Drain"/> reads an exact count without waiting on a clock.
    /// </summary>
    private sealed class Listener : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly ConcurrentQueue<string> _payloads = new();

        private Listener(NpgsqlConnection connection)
        {
            _connection = connection;
            _connection.Notification += (_, e) =>
            {
                if (e.Channel == Channel)
                    _payloads.Enqueue(e.Payload);
            };
        }

        public static async Task<Listener> Open()
        {
            var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
            await connection.OpenAsync();
            var listener = new Listener(connection);
            await using var listen = new NpgsqlCommand($"LISTEN {Channel}", connection);
            await listen.ExecuteNonQueryAsync();
            return listener;
        }

        /// <summary>
        /// Every payload heard since the last drain, after a round trip that brings in all those committed before it.
        /// </summary>
        public async Task<IReadOnlyList<string>> Drain()
        {
            using var bound = new CancellationTokenSource(Bound);
            await using var roundTrip = new NpgsqlCommand("SELECT 1", _connection);
            await roundTrip.ExecuteScalarAsync(bound.Token);
            var drained = new List<string>();
            while (_payloads.TryDequeue(out var payload))
                drained.Add(payload);
            return drained;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
