using System.Text.Json.Nodes;
using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// SQLite has no notifications, so the snapshot change listener polls the table by revision there: every write
/// that bumps a row's revision, a delete and a re-created row under the same id each reach its reader.
/// </summary>
[Property("adr", Adr)]
public class SqliteSnapshotChangePollTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "poll-user";

    private static readonly TimeSpan HearWithin = TimeSpan.FromSeconds(15);

    [Test]
    public async Task Sqlite_polling_reports_each_new_revision_a_delete_and_a_recreate()
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        var listener = host.Services.GetService<ISnapshotChangeListener>();
        listener
            .Should()
            .NotBeNull(
                $"AddStateMachines on SQLite registers a polling change listener. See {Adr}"
            );
        var hosted = host
            .Services.GetServices<IHostedService>()
            .Where(h => h is ISnapshotChangeListener)
            .ToList();
        foreach (var service in hosted)
            await service.StartAsync(CancellationToken.None);

        using var stop = new CancellationTokenSource();
        var heard = Channel.CreateUnbounded<long>();
        var listening = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reading = Task.Run(async () =>
        {
            try
            {
                await foreach (var change in listener!.ChangesAsync(stop.Token))
                    if (change is SnapshotChange.Row(var rowId))
                        heard.Writer.TryWrite(rowId);
                    else
                        listening.TrySetResult();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Stopped by the test.
            }
        });

        async Task<bool> Hears(long rowId)
        {
            using var timeout = new CancellationTokenSource(HearWithin);
            try
            {
                while (true)
                    if (await heard.Reader.ReadAsync(timeout.Token) == rowId)
                        return true;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return false;
            }
        }

        try
        {
            // The first change is a reload, once the poll has taken its baseline: every write after it is reported.
            await listening.Task.WaitAsync(HearWithin);

            var id = Guid.NewGuid();
            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Autosave(User, id, TestTurnstile.InitialJson)
                )
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
            var first = (await host.Rows(id)).Single().RowId;
            (await Hears(first)).Should().BeTrue($"the insert is revision 1. See {Adr}");

            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Advance(User, id, "Coin", new JsonObject { ["coin"] = "quarter" })
                )
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
            (await Hears(first)).Should().BeTrue("the advance is a new revision");

            using (var scope = host.Scope())
                await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, id);
            (await Hears(first)).Should().BeTrue("a delete is reported");

            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Autosave(User, id, TestTurnstile.InitialJson)
                )
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
            var second = (await host.Rows(id)).Single().RowId;
            second.Should().NotBe(first, "a re-created row is a new incarnation");
            (await Hears(second)).Should().BeTrue("the re-created row is reported");
        }
        finally
        {
            await stop.CancelAsync();
            await reading;
            foreach (var service in hosted)
                await service.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Polling_a_reader_that_watches_some_rows_reports_only_those()
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        var listener = (SnapshotChangeListener)
            host.Services.GetRequiredService<ISnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(100);

        var watchedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        foreach (var id in new[] { watchedId, otherId })
            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Autosave(User, id, TestTurnstile.InitialJson)
                )
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
        var watched = (await host.Rows(watchedId)).Single().RowId;
        var other = (await host.Rows(otherId)).Single().RowId;

        using var stop = new CancellationTokenSource();
        var heard = Channel.CreateUnbounded<long>();
        var listening = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reading = Task.Run(async () =>
        {
            try
            {
                await foreach (
                    var change in listener.ChangesAsync(stop.Token, () => new([watched]))
                )
                    if (change is SnapshotChange.Row(var rowId))
                        heard.Writer.TryWrite(rowId);
                    else
                        listening.TrySetResult();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });

        try
        {
            await listening.Task.WaitAsync(HearWithin);
            foreach (var id in new[] { otherId, watchedId })
                using (var scope = host.Scope())
                    (
                        await host.Service(scope, "turnstile")
                            .Advance(User, id, "Coin", new JsonObject { ["coin"] = "quarter" })
                    )
                        .Should()
                        .BeOfType<AdvanceOutcome.Advanced>();

            using var timeout = new CancellationTokenSource(HearWithin);
            (await heard.Reader.ReadAsync(timeout.Token))
                .Should()
                .Be(watched, $"only the rows a reader watches are polled and reported. See {Adr}");
            // negative-wait: several polls pass, and the unwatched row must not be reported.
            await Task.Delay(500);
            heard
                .Reader.TryRead(out var more)
                .Should()
                .BeFalse($"row {other} is not watched, yet {more} was reported");
        }
        finally
        {
            await stop.CancelAsync();
            await reading;
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Polling_stops_when_the_last_reader_leaves_and_starts_again_with_the_next()
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        var listener = (SnapshotChangeListener)
            host.Services.GetRequiredService<ISnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(100);

        async Task<(CancellationTokenSource Stop, Task Reading)> ReadAsync()
        {
            var stop = new CancellationTokenSource();
            var listening = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var reading = Task.Run(async () =>
            {
                try
                {
                    await foreach (var change in listener.ChangesAsync(stop.Token))
                        if (change is SnapshotChange.Reload)
                            listening.TrySetResult();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
            await listening.Task.WaitAsync(HearWithin);
            return (stop, reading);
        }

        try
        {
            var (first, firstReading) = await ReadAsync();
            listener.IsPolling.Should().BeTrue("a reader is reading");

            await first.CancelAsync();
            await firstReading;
            var deadline = DateTime.UtcNow + HearWithin;
            while (listener.IsPolling && DateTime.UtcNow < deadline)
                // allowed-delay: polls until the listener's loop has ended.
                await Task.Delay(50);
            listener
                .IsPolling.Should()
                .BeFalse($"with no reader left, nothing polls the table. See {Adr}");

            var (second, secondReading) = await ReadAsync();
            listener.IsPolling.Should().BeTrue("the next reader starts it again, with a reload");
            await second.CancelAsync();
            await secondReading;
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task The_hub_lets_go_of_the_listener_when_its_last_watch_ends()
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        var listener = (SnapshotChangeListener)
            host.Services.GetRequiredService<ISnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(100);
        var hub = host.Services.GetRequiredService<SnapshotWatchHub>();

        var id = Guid.NewGuid();
        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        var rowId = (await host.Rows(id)).Single().RowId;

        try
        {
            var watch = hub.Watch(rowId);
            using (var first = new CancellationTokenSource(HearWithin))
                (await watch.Changes.ReadAsync(first.Token)).RowId.Should().Be(rowId);
            listener.IsPolling.Should().BeTrue("a watch is open");

            await watch.DisposeAsync();
            var deadline = DateTime.UtcNow + HearWithin;
            while (listener.IsPolling && DateTime.UtcNow < deadline)
                // allowed-delay: polls until the listener's loop has ended.
                await Task.Delay(50);
            listener
                .IsPolling.Should()
                .BeFalse($"no watch is open, so nothing polls the table. See {Adr}");

            // A new watch starts it again and still hears the next write.
            var again = hub.Watch(rowId);
            using (var first = new CancellationTokenSource(HearWithin))
                await again.Changes.ReadAsync(first.Token);
            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Advance(User, id, "Coin", new JsonObject { ["coin"] = "quarter" })
                )
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
            using var next = new CancellationTokenSource(HearWithin);
            (await again.Changes.ReadAsync(next.Token)).Revision.Should().BeGreaterThan(1);
            await again.DisposeAsync();
        }
        finally
        {
            await hub.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public Task A_row_watched_and_deleted_between_two_polls_ends_its_watch_with_gone() =>
        WatchedAndDeletedBetweenTwoPolls(watchAll: false);

    [Test]
    public Task A_row_watched_and_deleted_between_two_polls_ends_its_watch_with_gone_while_every_row_is_watched() =>
        WatchedAndDeletedBetweenTwoPolls(watchAll: true);

    // With watchAll, an operator's watch on every row is open too, so the poll reads every row; the row still joins
    // and leaves between two of those polls, which no poll of every row ever reads.
    private static async Task WatchedAndDeletedBetweenTwoPolls(bool watchAll)
    {
        using var host = InstanceHost.Create(StoreProvider.Sqlite);
        var listener = (SnapshotChangeListener)
            host.Services.GetRequiredService<ISnapshotChangeListener>();
        listener.PollInterval = TimeSpan.FromMilliseconds(200);
        var hub = host.Services.GetRequiredService<SnapshotWatchHub>();

        async Task<(Guid Id, long RowId)> Saved()
        {
            var id = Guid.NewGuid();
            using (var scope = host.Scope())
                (
                    await host.Service(scope, "turnstile")
                        .Autosave(User, id, TestTurnstile.InitialJson)
                )
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
            return (id, (await host.Rows(id)).Single().RowId);
        }

        try
        {
            // Another watch keeps the poll running, with a baseline that has none of the rows below.
            var (_, other) = await Saved();
            var keeping = hub.Watch(other);
            using (var first = new CancellationTokenSource(HearWithin))
                await keeping.Changes.ReadAsync(first.Token);
            await using var all = watchAll ? hub.WatchAll(null) : null;
            if (all is not null)
                using (var first = new CancellationTokenSource(HearWithin))
                    (await all.Changes.ReadAsync(first.Token))
                        .Kind.Should()
                        .Be(SnapshotRowChangeKind.Reload);

            // Each row joins the watched set and is deleted at once, most often before the next poll reads it.
            for (var i = 0; i < 3; i++)
            {
                var (id, rowId) = await Saved();
                var watch = hub.Watch(rowId);
                using (var first = new CancellationTokenSource(HearWithin))
                    (await watch.Changes.ReadAsync(first.Token))
                        .Kind.Should()
                        .Be(SnapshotRowChangeKind.Changed);
                using (var scope = host.Scope())
                    await scope
                        .ServiceProvider.GetRequiredService<ISnapshotStore>()
                        .Delete(User, id);

                // Within two polls, and a second for the hub's re-read on a slow machine.
                using var next = new CancellationTokenSource(
                    listener.PollInterval * 2 + TimeSpan.FromSeconds(1)
                );
                (await watch.Changes.ReadAsync(next.Token))
                    .Kind.Should()
                    .Be(
                        SnapshotRowChangeKind.Gone,
                        $"the next poll reports a watched row that is missing, though it never saw it. See {Adr}"
                    );
                await watch.DisposeAsync();
            }
            await keeping.DisposeAsync();
        }
        finally
        {
            await hub.StopAsync(CancellationToken.None);
            await listener.StopAsync(CancellationToken.None);
        }
    }
}
