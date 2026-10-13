using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A Postgres host chooses how it hears changes (<see cref="StateMachineOptions.ChangeFeed"/>). Notify listens for the
/// notice the database sends for every write; Poll reads the watched rows by revision, as SQLite does, and delivers
/// every change and delete without one. The database stops sending the notices for a session whose
/// <c>trax.snapshot_notify</c> is <c>'off'</c>, so the writes stop paying for them; a host that listens on such a
/// database refuses to start, and one that polls while the database still notifies starts and warns.
/// </summary>
[Property("adr", Adr)]
public class PostgresChangeFeedTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "feed-user";

    private static readonly TimeSpan Within = TimeSpan.FromSeconds(15);

    // The shared test database, with the notices off for every session the connection string opens.
    private static string NoticesOff =>
        PostgresSetup.ConnectionString + ";Options=-c trax.snapshot_notify=off";

    private static async Task<(Guid Id, long RowId)> Saved(InstanceHost host)
    {
        var id = Guid.NewGuid();
        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        return (id, await host.RowId(id));
    }

    private static async Task Coin(InstanceHost host, Guid id)
    {
        using var scope = host.Scope();
        (
            await host.Service(scope, "turnstile")
                .Advance(User, id, "Coin", new JsonObject { ["coin"] = "quarter" })
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }

    private static async Task<SnapshotRowChange> Next(SnapshotWatch watch)
    {
        using var timeout = new CancellationTokenSource(Within);
        return await watch.Changes.ReadAsync(timeout.Token);
    }

    // Whether a poll reads the row within the wait.
    private static async Task<bool> Polled(SnapshotChangeListener listener, long rowId)
    {
        var until = DateTime.UtcNow + Within;
        while (DateTime.UtcNow < until)
        {
            if (listener.LastPolled.Contains(rowId))
                return true;
            // allowed-delay: waits for the listener's next poll, which runs every 50 ms here.
            await Task.Delay(20);
        }
        return false;
    }

    // The host's registered startup check of its change feed.
    private static ChangeFeedStartupValidator Validator(InstanceHost host) =>
        host.Services.GetServices<IHostedService>().OfType<ChangeFeedStartupValidator>().Single();

    // Records the warnings the host's startup check logs.
    private static Action<IServiceCollection> Logging(
        RecordingLogger<ChangeFeedStartupValidator> logger
    ) => services => services.AddSingleton<ILogger<ChangeFeedStartupValidator>>(logger);

    [Test]
    public async Task A_write_on_a_session_with_the_notices_off_sends_no_notice()
    {
        using var off = InstanceHost.Create(
            StoreProvider.Postgres,
            postgresConnectionString: NoticesOff
        );
        using var on = InstanceHost.Create(StoreProvider.Postgres);
        await using var notices = await SnapshotNotices.Listen();

        var (silentId, silent) = await Saved(off);
        await Coin(off, silentId);
        using (var scope = off.Scope())
            await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, silentId);
        var (_, heard) = await Saved(on);

        var payloads = await notices.Collect();
        payloads
            .Should()
            .NotContain(
                silent.ToString(CultureInfo.InvariantCulture),
                $"an insert, update or delete with trax.snapshot_notify off sends no notice. See {Adr}"
            );
        payloads
            .Should()
            .ContainSingle(
                p => p == heard.ToString(CultureInfo.InvariantCulture),
                "a session that leaves the setting alone still notifies"
            );
    }

    [Test]
    public async Task A_polling_host_delivers_each_write_and_the_delete_without_notices()
    {
        using var host = InstanceHost.Create(
            StoreProvider.Postgres,
            options: o => o.ChangeFeed = StateMachineChangeFeed.Poll,
            postgresConnectionString: NoticesOff
        );
        var listener = host.Services.GetRequiredService<SnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(50);
        listener
            .Polls.Should()
            .BeTrue($"a host that chose Poll opens no listening session. See {Adr}");
        var hub = host.Services.GetRequiredService<SnapshotWatchHub>();

        try
        {
            await Validator(host).StartingAsync(CancellationToken.None);

            var (id, rowId) = await Saved(host);
            var watch = hub.Watch(rowId);
            (await Next(watch)).Revision.Should().Be(1);

            await Coin(host, id);
            var next = await Next(watch);
            next.Kind.Should().Be(SnapshotRowChangeKind.Changed);
            next.Revision.Should()
                .Be(2, $"the poll reports the new revision with no notice sent. See {Adr}");

            using (var scope = host.Scope())
                await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, id);
            (await Next(watch))
                .Kind.Should()
                .Be(SnapshotRowChangeKind.Gone, "the poll reports the delete");
            await watch.DisposeAsync();
        }
        finally
        {
            await hub.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task A_polling_feed_on_one_machine_reads_only_that_machines_rows()
    {
        using var host = InstanceHost.Create(
            StoreProvider.Postgres,
            options: o => o.ChangeFeed = StateMachineChangeFeed.Poll
        );
        var listener = host.Services.GetRequiredService<SnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(50);
        var hub = host.Services.GetRequiredService<SnapshotWatchHub>();

        var (turnstileId, turnstile) = await Saved(host);
        var reminderId = Guid.NewGuid();
        await host.SeedDraft(User, reminderId, ReminderMachine.Json("Idle"));
        var reminder = await host.RowId(reminderId);

        try
        {
            await using (var feed = hub.WatchAll("turnstile"))
            {
                (await Next(feed)).Kind.Should().Be(SnapshotRowChangeKind.Reload);
                listener.LastPolled.Should().Contain(turnstile);
                listener
                    .LastPolled.Should()
                    .NotContain(
                        reminder,
                        $"a feed on one machine polls that machine's rows only, not the whole table. See {Adr}"
                    );

                await Coin(host, turnstileId);
                var next = await Next(feed);
                next.RowId.Should().Be(turnstile);
                next.Revision.Should()
                    .Be(2, "the narrower poll still reports the machine's writes");
            }

            // The listener's loop may outlive the first feed by a poll, so the wider read is waited for.
            await using (var everything = hub.WatchAll(null))
            {
                (await Next(everything)).Kind.Should().Be(SnapshotRowChangeKind.Reload);
                (await Polled(listener, reminder))
                    .Should()
                    .BeTrue("a feed on every machine polls every row");
            }
        }
        finally
        {
            await hub.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task A_listening_host_whose_database_sends_no_notices_refuses_to_start()
    {
        using var host = InstanceHost.Create(
            StoreProvider.Postgres,
            postgresConnectionString: NoticesOff
        );

        var act = () => Validator(host).StartingAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*ChangeFeed is Notify*trax.snapshot_notify*Poll*"
        );
    }

    [Test]
    public async Task A_polling_host_whose_database_still_notifies_starts_and_warns()
    {
        var logger = new RecordingLogger<ChangeFeedStartupValidator>();
        using var host = InstanceHost.Create(
            StoreProvider.Postgres,
            options: o => o.ChangeFeed = StateMachineChangeFeed.Poll,
            services: Logging(logger)
        );

        await Validator(host).StartingAsync(CancellationToken.None);

        logger
            .Warnings.Should()
            .ContainSingle(
                $"polling is correct while the database notifies, but every write still pays for it. See {Adr}"
            )
            .Which.Should()
            .Contain("trax.snapshot_notify = 'off'");
    }

    [Test]
    public async Task A_listening_host_whose_database_notifies_starts_and_hears_each_write()
    {
        var logger = new RecordingLogger<ChangeFeedStartupValidator>();
        using var host = InstanceHost.Create(StoreProvider.Postgres, services: Logging(logger));
        var listener = host.Services.GetRequiredService<SnapshotChangeListener>();
        listener.Polls.Should().BeFalse("Notify is the default and listens on Postgres");
        var hub = host.Services.GetRequiredService<SnapshotWatchHub>();

        try
        {
            await Validator(host).StartingAsync(CancellationToken.None);
            logger.Warnings.Should().BeEmpty();

            var (id, rowId) = await Saved(host);
            await using var watch = hub.Watch(rowId);
            (await Next(watch)).Revision.Should().Be(1);
            await Coin(host, id);
            (await Next(watch))
                .Revision.Should()
                .Be(2, $"the notice delivers the write. See {Adr}");
        }
        finally
        {
            await hub.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public void The_change_feed_refuses_a_value_it_does_not_name()
    {
        var act = () => new StateMachineOptions { ChangeFeed = (StateMachineChangeFeed)7 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                    return [.. _warnings];
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
                lock (_warnings)
                    _warnings.Add(formatter(state, exception));
        }
    }
}
