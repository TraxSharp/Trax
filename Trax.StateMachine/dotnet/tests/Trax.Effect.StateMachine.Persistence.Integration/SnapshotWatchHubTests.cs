using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The host's snapshot watch hub, the seam the push surfaces read: a watcher of a row gets the row as it is, then
/// each newer revision, re-read from the database; a delete ends the watch with gone, a row the host cannot read
/// ends only its own watchers, and every watch is counted until it is released. On Postgres the hub hears the
/// notices; on SQLite it hears the poll.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class SnapshotWatchHubTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "hub-user";

    private static readonly TimeSpan Within = TimeSpan.FromSeconds(15);

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = InstanceHost.Create(provider);
        _host.Services.GetRequiredService<SnapshotChangeListener>().PollInterval =
            TimeSpan.FromMilliseconds(50);
    }

    // The host's hub; the container owns it and disposes it with the host.
    private SnapshotWatchHub Hub => _host.Services.GetRequiredService<SnapshotWatchHub>();

    [TearDown]
    public void TearDown() => _host.Dispose();

    private async Task<(Guid Id, long RowId)> Saved()
    {
        var id = Guid.NewGuid();
        using (var scope = _host.Scope())
            (await _host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        return (id, await _host.RowId(id));
    }

    private async Task Coin(Guid id)
    {
        using var scope = _host.Scope();
        (
            await _host
                .Service(scope, "turnstile")
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

    // Whether the watch ends within the bound, once whatever it still holds is read.
    private static async Task<bool> Ends(SnapshotWatch watch)
    {
        using var timeout = new CancellationTokenSource(Within);
        try
        {
            await foreach (var _ in watch.Changes.ReadAllAsync(timeout.Token)) { }
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    [Test]
    public async Task The_first_change_is_the_row_as_it_is_and_each_write_follows()
    {
        var (id, rowId) = await Saved();
        await using var watch = Hub.Watch(rowId);

        var first = await Next(watch);
        first.Kind.Should().Be(SnapshotRowChangeKind.Changed);
        first.Revision.Should().Be(1, $"the first change is the row as it is now. See {Adr}");
        first.State.Should().Be("Locked");
        first.Id.Should().Be(id);
        first.Machine.Should().Be("turnstile");
        first.UserKey.Should().Be(User);

        await Coin(id);

        var next = await Next(watch);
        next.Kind.Should().Be(SnapshotRowChangeKind.Changed);
        next.Revision.Should()
            .Be(2, $"each write reaches the watcher as the re-read row. See {Adr}");
        next.State.Should().Be("Unlocked");
        JsonNode.Parse(next.Context!)!["paidWith"]!.GetValue<string>().Should().Be("quarter");
    }

    [Test]
    public async Task Delete_ends_the_watch_with_gone_and_a_recreate_does_not_reach_it()
    {
        var (id, rowId) = await Saved();
        var watch = Hub.Watch(rowId);
        (await Next(watch)).Revision.Should().Be(1);

        using (var scope = _host.Scope())
            await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, id);

        var gone = await Next(watch);
        gone.Kind.Should()
            .Be(SnapshotRowChangeKind.Gone, $"a deleted row ends its watch. See {Adr}");
        gone.Deleted.Should().BeTrue();
        (await Ends(watch)).Should().BeTrue("gone is the last change");
        Hub.ActiveWatches.Should().Be(0, "a watch the hub ends is released");

        var (_, recreated) = await Saved();
        recreated.Should().NotBe(rowId, "a re-created row is a new incarnation");
        watch
            .Changes.TryRead(out _)
            .Should()
            .BeFalse($"a stream on the old incarnation never sees the new one. See {Adr}");
        await watch.DisposeAsync();
    }

    [Test]
    public async Task A_row_the_host_cannot_parse_ends_that_stream_only()
    {
        var (brokenId, broken) = await Saved();
        var (healthyId, healthy) = await Saved();
        var brokenWatch = Hub.Watch(broken);
        await using var healthyWatch = Hub.Watch(healthy);
        (await Next(brokenWatch)).Revision.Should().Be(1);
        (await Next(healthyWatch)).Revision.Should().Be(1);

        // As a newer version of the machine would leave it: a state this host's machine does not have.
        await _host.Execute(
            $"UPDATE {_host.DraftTable} SET state = 'StateFromANewerVersion' WHERE row_id = @p0",
            broken
        );

        var unreadable = await Next(brokenWatch);
        unreadable
            .Kind.Should()
            .Be(
                SnapshotRowChangeKind.Unreadable,
                $"a row this host cannot read ends its watch. See {Adr}"
            );
        unreadable.Context.Should().BeNull("nothing of a row the host cannot read is handed on");
        (await Ends(brokenWatch)).Should().BeTrue();

        await Coin(healthyId);
        (await Next(healthyWatch))
            .Revision.Should()
            .Be(2, $"one unreadable row does not stop the hub for the others. See {Adr}");
        Hub.ActiveWatches.Should().Be(1);
        brokenId.Should().NotBe(healthyId);
        await brokenWatch.DisposeAsync();
    }

    [Test]
    public async Task Watches_are_counted_and_released()
    {
        var (_, rowId) = await Saved();
        var watches = new List<SnapshotWatch>
        {
            Hub.Watch(rowId),
            Hub.Watch(rowId),
            Hub.Watch(rowId),
            Hub.WatchAll("turnstile"),
        };
        Hub.ActiveWatches.Should().Be(4, $"every open watch is counted. See {Adr}");
        foreach (var watch in watches.Take(3))
            (await Next(watch)).Revision.Should().Be(1);

        foreach (var watch in watches)
            await watch.DisposeAsync();
        await watches[0].DisposeAsync();

        Hub.ActiveWatches.Should()
            .Be(0, "a disposed watch is released once, however often it is disposed");
        foreach (var watch in watches)
            (await Ends(watch)).Should().BeTrue("a released watch ends");
    }

    [Test]
    public async Task Watching_every_row_reports_writes_and_deletes()
    {
        await using var all = Hub.WatchAll("turnstile");
        (await Next(all))
            .Kind.Should()
            .Be(SnapshotRowChangeKind.Reload, "a watch on every row starts by listing again");

        var (id, rowId) = await Saved();
        var created = await Next(all);
        created.Kind.Should().Be(SnapshotRowChangeKind.Changed);
        created.RowId.Should().Be(rowId);
        created.State.Should().Be("Locked");

        using (var scope = _host.Scope())
            await scope.ServiceProvider.GetRequiredService<ISnapshotStore>().Delete(User, id);
        var gone = await Next(all);
        gone.Kind.Should().Be(SnapshotRowChangeKind.Gone);
        gone.RowId.Should().Be(rowId);
        gone.Machine.Should().Be("turnstile", "the hub had read the row before it went");
    }
}
