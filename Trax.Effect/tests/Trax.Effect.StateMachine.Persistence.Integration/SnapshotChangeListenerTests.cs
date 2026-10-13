using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The host's snapshot change listener on Postgres: one listening session per host whatever the number of
/// readers, never holding a transaction, reconnecting when its session is killed, reading nothing for a notice by
/// itself, metering a notice it cannot read and reporting how full the notification queue is.
/// </summary>
[Property("adr", Adr)]
public class SnapshotChangeListenerTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string ApplicationName = "trax_snapshot_listener";

    private const string User = "listener-user";

    /// <summary>How long a test waits for a row id it expects the listener to hand on.</summary>
    private static readonly TimeSpan HearWithin = TimeSpan.FromSeconds(15);

    private InstanceHost _host = null!;
    private readonly List<Feed> _feeds = [];
    private readonly List<IHostedService> _started = [];
    private CountingDataContextFactory? _counting;

    [SetUp]
    public void SetUp() =>
        _host = InstanceHost.Create(
            StoreProvider.Postgres,
            services: CountingDataContextFactory.Install
        );

    [TearDown]
    public async Task TearDown()
    {
        foreach (var feed in _feeds)
            await feed.DisposeAsync();
        _feeds.Clear();
        foreach (var hosted in _started)
            await hosted.StopAsync(CancellationToken.None);
        _started.Clear();
        _host.Dispose();
    }

    /// <summary>The host's listener, started, with <paramref name="readers"/> readers of its row ids.</summary>
    private async Task<List<Feed>> Listen(int readers = 1)
    {
        var listener = _host.Services.GetService<ISnapshotChangeListener>();
        listener
            .Should()
            .NotBeNull(
                $"AddStateMachines on Postgres registers the snapshot change listener. See {Adr}"
            );
        foreach (var hosted in _host.Services.GetServices<IHostedService>())
            if (hosted is ISnapshotChangeListener)
            {
                await hosted.StartAsync(CancellationToken.None);
                _started.Add(hosted);
            }
        _counting = _host.Services.GetRequiredService<CountingDataContextFactory>();

        var started = new List<Feed>();
        for (var i = 0; i < readers; i++)
            started.Add(Feed.Start(listener!));
        _feeds.AddRange(started);

        // Every reader is told to reload first, once the listener is listening.
        foreach (var feed in started)
            (await feed.HearsReload(HearWithin))
                .Should()
                .BeTrue(
                    $"a reader's first change is a reload, sent once the listener listens. See {Adr}"
                );

        // A write each reader hears proves the listener is subscribed before the test goes on.
        var probe = await Seed();
        await Touch(probe);
        foreach (var feed in started)
            (await feed.Hears(probe, HearWithin))
                .Should()
                .BeTrue("the listener hands on the row id of a committed write");
        return started;
    }

    private async Task<long> Seed()
    {
        var id = Guid.NewGuid();
        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Insert(
                    User,
                    id,
                    new Snapshot
                    {
                        Machine = "turnstile",
                        Version = 1,
                        State = "Locked",
                        Context = new JsonObject(),
                    }
                );
        return (await _host.Rows(id)).Single().RowId;
    }

    // A committed write by raw SQL, on a connection of the test's own, so it reads nothing through the host.
    private static async Task Touch(long rowId)
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE trax.snapshot_draft SET concurrency_token = gen_random_uuid() WHERE row_id = @row";
        command.Parameters.AddWithValue("@row", rowId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<(int Pid, string? State)>> ListenerSessions()
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT pid, state FROM pg_stat_activity "
            + "WHERE application_name = @name AND datname = current_database()";
        command.Parameters.AddWithValue("@name", ApplicationName);
        var sessions = new List<(int, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            sessions.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return sessions;
    }

    private static async Task<long> TerminateListenerSessions()
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM (SELECT pg_terminate_backend(pid) FROM pg_stat_activity "
            + "WHERE application_name = @name AND datname = current_database()) terminated";
        command.Parameters.AddWithValue("@name", ApplicationName);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task The_listener_reconnects_after_termination_and_its_subscribers_are_told_to_reload()
    {
        var feed = (await Listen()).Single();

        (await TerminateListenerSessions())
            .Should()
            .Be(1, $"the host listens on one session named {ApplicationName}. See {Adr}");

        (await feed.HearsReload(HearWithin))
            .Should()
            .BeTrue(
                $"once it listens again, its readers are told to reload, since a write may have been missed. See {Adr}"
            );

        var after = await Seed();
        await Touch(after);
        (await feed.Hears(after, HearWithin))
            .Should()
            .BeTrue($"the listener subscribes again and goes on handing row ids on. See {Adr}");
    }

    [Test]
    public async Task A_write_during_the_listener_outage_reaches_the_subscriber_after_reconnect()
    {
        await Listen();
        var row = await Seed();
        var hub = _host.Services.GetRequiredService<SnapshotWatchHub>();
        await using var watch = hub.Watch(row);
        var first = await Next(watch);
        first.Kind.Should().Be(SnapshotRowChangeKind.Changed);

        (await TerminateListenerSessions()).Should().Be(1);
        await Touch(row);

        var next = await Next(watch);
        next.Kind.Should().Be(SnapshotRowChangeKind.Changed);
        next.Revision.Should()
            .BeGreaterThan(
                first.Revision,
                $"a write the listener missed while it reconnected is found by the re-read its reload asks for. See {Adr}"
            );
    }

    private static async Task<SnapshotRowChange> Next(SnapshotWatch watch)
    {
        using var timeout = new CancellationTokenSource(HearWithin);
        return await watch.Changes.ReadAsync(timeout.Token);
    }

    [Test]
    public async Task One_listening_session_per_host_whatever_the_subscriber_count()
    {
        await Listen(readers: 5);

        var sessions = await ListenerSessions();
        sessions
            .Should()
            .ContainSingle($"five readers share the host's one listening session. See {Adr}");
        sessions[0]
            .State.Should()
            .NotBe("idle in transaction", "the listener never holds a transaction open");
    }

    [Test]
    public async Task A_notice_for_a_row_nobody_here_watches_reads_nothing()
    {
        var feed = (await Listen()).Single();
        var unwatched = await Seed();
        var marker = await Seed();
        var before = _counting!.Count;

        await SnapshotNotices.Notify(SnapshotNotices.Channel, unwatched.ToString());
        await Touch(marker);
        (await feed.Hears(marker, HearWithin)).Should().BeTrue();

        _counting
            .Count.Should()
            .Be(
                before,
                $"the listener hands on a row id and reads nothing; only a host with subscribers to the row re-reads it. See {Adr}"
            );
    }

    [Test]
    public async Task A_malformed_notice_is_metered_and_ignored()
    {
        long malformed = 0;
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == StateMachineInstruments.MeterName
                && instrument.Name == StateMachineInstruments.SnapshotNoticeMalformed
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<long>(
            (_, value, _, _) => Interlocked.Add(ref malformed, value)
        );
        meters.SetMeasurementEventCallback<int>(
            (_, value, _, _) => Interlocked.Add(ref malformed, value)
        );
        meters.Start();

        var feed = (await Listen()).Single();
        var marker = await Seed();

        await SnapshotNotices.Notify(SnapshotNotices.Channel, "junk");
        await Touch(marker);
        (await feed.Hears(marker, HearWithin))
            .Should()
            .BeTrue("a malformed notice does not stop the listener");

        Interlocked
            .Read(ref malformed)
            .Should()
            .Be(1, $"a notice whose payload is not a row id is metered and dropped. See {Adr}");
    }

    [Test]
    public async Task Notification_queue_usage_is_reported_for_alerting()
    {
        var reported = new TaskCompletionSource<double>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var meters = new MeterListener();
        meters.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == StateMachineInstruments.MeterName
                && instrument.Name == StateMachineInstruments.NotificationQueueUsage
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meters.SetMeasurementEventCallback<double>(
            (_, value, _, _) => reported.TrySetResult(value)
        );
        meters.SetMeasurementEventCallback<long>((_, value, _, _) => reported.TrySetResult(value));
        meters.Start();

        await Listen();
        meters.RecordObservableInstruments();

        var usage = await reported.Task.WaitAsync(HearWithin);
        usage
            .Should()
            .BeInRange(
                0,
                1,
                $"pg_notification_queue_usage() is reported so a filling queue can be alerted on. See {Adr}"
            );
    }

    [Test]
    public async Task Notification_queue_usage_is_read_every_interval_while_notices_keep_arriving()
    {
        var interval = SnapshotChangeListener.QueueUsageInterval;
        SnapshotChangeListener.QueueUsageInterval = TimeSpan.FromMilliseconds(200);
        using var stop = new CancellationTokenSource();
        Task flood = Task.CompletedTask;
        try
        {
            await Listen();

            // A notice every few milliseconds for a row nobody watches: the session is never idle for an interval.
            var flooding = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            flood = Task.Run(async () =>
            {
                await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
                await connection.OpenAsync();
                await using var notify = connection.CreateCommand();
                notify.CommandText =
                    $"SELECT pg_notify('{SnapshotNotices.Channel}', '999999999999')";
                while (!stop.IsCancellationRequested)
                {
                    await notify.ExecuteNonQueryAsync();
                    flooding.TrySetResult();
                }
            });
            await flooding.Task.WaitAsync(HearWithin);

            // Two readings of the queue on the listener's session, both after the flood began.
            var since = await DatabaseNow();
            var first = await QueueReadAfter(since);
            first.Should().NotBeNull($"the queue is read while notices keep arriving. See {Adr}");
            (await QueueReadAfter(first!.Value))
                .Should()
                .NotBeNull("and read again an interval later, however busy the session is");
        }
        finally
        {
            await stop.CancelAsync();
            await flood;
            SnapshotChangeListener.QueueUsageInterval = interval;
        }
    }

    private static async Task<DateTime> DatabaseNow()
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT clock_timestamp()";
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }

    // When the listener's session last started reading the queue, once that is after since; null if it does not
    // within the bound.
    private static async Task<DateTime?> QueueReadAfter(DateTime since)
    {
        await using var connection = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT query_start FROM pg_stat_activity WHERE application_name = @name "
            + "AND datname = current_database() AND query = 'SELECT pg_notification_queue_usage()' "
            + "AND query_start > @since";
        command.Parameters.AddWithValue("@name", ApplicationName);
        command.Parameters.AddWithValue("@since", since);
        var deadline = DateTime.UtcNow + HearWithin;
        while (DateTime.UtcNow < deadline)
        {
            if (await command.ExecuteScalarAsync() is DateTime started)
                return started;
            // allowed-delay: polls pg_stat_activity until the session's next reading shows.
            await Task.Delay(50);
        }
        return null;
    }

    [Test]
    public async Task A_forged_notice_for_an_unchanged_row_sends_nothing()
    {
        await Listen();
        var row = await Seed();
        var sentinel = await Seed();
        var hub = _host.Services.GetRequiredService<SnapshotWatchHub>();
        await using var watch = hub.Watch(row);
        (await Next(watch)).Revision.Should().Be(1);
        await using var sentinelWatch = hub.Watch(sentinel);
        (await Next(sentinelWatch)).Revision.Should().Be(1);

        await SnapshotNotices.Notify(SnapshotNotices.Channel, row.ToString());
        // Two later writes to another watched row, each read in a burst of its own after the forged one's.
        await Touch(sentinel);
        (await Next(sentinelWatch)).Revision.Should().Be(2);
        await Touch(sentinel);
        (await Next(sentinelWatch)).Revision.Should().Be(3);

        watch
            .Changes.TryRead(out var sent)
            .Should()
            .BeFalse(
                $"a notice is a hint: the re-read finds the revision unchanged, so nothing is sent (got {sent}). See {Adr}"
            );
    }

    [Test]
    public async Task N_subscribers_and_M_notices_on_one_row_cost_one_coalesced_read()
    {
        const int Subscribers = 10;
        const int Notices = 20;
        var feed = (await Listen()).Single();
        var row = await Seed();
        var hub = _host.Services.GetRequiredService<SnapshotWatchHub>();
        var watches = Enumerable.Range(0, Subscribers).Select(_ => hub.Watch(row)).ToList();
        try
        {
            foreach (var watch in watches)
                (await Next(watch)).Revision.Should().Be(1);

            // The next read waits until the test lets it go, so every notice below lands while it is held.
            var entered = _counting!.Hold();
            var before = _counting.Count;
            await Touch(row);
            await entered.WaitAsync(HearWithin);
            await Touch(row);
            for (var i = 0; i < Notices; i++)
                await SnapshotNotices.Notify(SnapshotNotices.Channel, row.ToString());
            var marker = await Seed();
            await Touch(marker);
            (await feed.Hears(marker, HearWithin))
                .Should()
                .BeTrue("every notice before the marker has reached the hub too");
            _counting.Release();

            foreach (var watch in watches)
            {
                var latest = await Next(watch);
                while (latest.Revision < 3)
                    latest = await Next(watch);
                latest
                    .Revision.Should()
                    .Be(
                        3,
                        $"each watcher gets the row coalesced to its latest revision. See {Adr}"
                    );
            }

            (_counting.Count - before)
                .Should()
                .BeLessThanOrEqualTo(
                    3,
                    $"{Subscribers} watchers and {Notices + 2} notices on one row cost the read that was held, one more "
                        + $"for the notices that landed during it, and at most one for a notice the hub took late. See {Adr}"
                );
        }
        finally
        {
            _counting?.Release();
            foreach (var watch in watches)
                await watch.DisposeAsync();
        }

        hub.ActiveWatches.Should().Be(0, "every watch is released when it is disposed");
    }

    /// <summary>One reader of the listener's changes, buffered so a test can wait for a given one.</summary>
    private sealed class Feed : IAsyncDisposable
    {
        private readonly Channel<SnapshotChange> _heard = Channel.CreateUnbounded<SnapshotChange>();
        private readonly CancellationTokenSource _stop = new();
        private Task _loop = Task.CompletedTask;

        public static Feed Start(ISnapshotChangeListener listener)
        {
            var feed = new Feed();
            feed._loop = feed.Read(listener);
            return feed;
        }

        private async Task Read(ISnapshotChangeListener listener)
        {
            try
            {
                await foreach (var change in listener.ChangesAsync(_stop.Token))
                    _heard.Writer.TryWrite(change);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Disposed.
            }
            finally
            {
                _heard.Writer.TryComplete();
            }
        }

        /// <summary>Reads until <paramref name="rowId"/> is heard (true) or <paramref name="within"/> passes (false).</summary>
        public Task<bool> Hears(long rowId, TimeSpan within) =>
            HearsAny(c => c is SnapshotChange.Row(var heard) && heard == rowId, within);

        /// <summary>Reads until a reload is heard (true) or <paramref name="within"/> passes (false).</summary>
        public Task<bool> HearsReload(TimeSpan within) =>
            HearsAny(c => c is SnapshotChange.Reload, within);

        private async Task<bool> HearsAny(Func<SnapshotChange, bool> match, TimeSpan within)
        {
            using var timeout = new CancellationTokenSource(within);
            try
            {
                while (await _heard.Reader.WaitToReadAsync(timeout.Token))
                while (_heard.Reader.TryRead(out var heard))
                    if (match(heard))
                        return true;
                return false;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            _stop.Dispose();
        }
    }
}
