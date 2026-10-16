using System.Diagnostics;
using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Services;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// How a node answers an id under the reserved machine-document prefix that is not one of its own: an id of a machine
/// it does not serve is retried while the store holds it active and reloaded once it was collected; an id of an older
/// stored version tells the client to reload; and an id it has not seen is answered at once from what it last read of
/// the store, while the store is read again in the background at most once per interval, the oldest of the ids waiting
/// for that read forgotten first past the bound.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class MachineDocumentRegistryTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly string OwnHash = new('a', MachineDocuments.IrHashLength);
    private static readonly string OldHash = new('b', MachineDocuments.IrHashLength);
    private static readonly string NewHash = new('c', MachineDocuments.IrHashLength);

    private static string Id(string machine, string operation, string irHash) =>
        $"{MachineDocuments.ReservedIdPrefix}{machine}_{operation}_{irHash}{new string('0', MachineDocuments.TextHashLength)}";

    private static readonly string OwnGet = Id("wizard", "get", OwnHash);
    private static readonly string OldGet = Id("wizard", "get", OldHash);

    // A node serving `wizard` at version 2, whose store also held version 1.
    private static MachineDocumentRegistry Registry(TimeSpan? interval = null)
    {
        var registry = new MachineDocumentRegistry
        {
            RefreshInterval = interval ?? TimeSpan.FromMilliseconds(200),
        };
        registry.Publish(
            [new MachineDocument(OwnGet, "wizard", "get", "query { }", OwnHash)],
            new Dictionary<string, int> { ["wizard"] = 2 },
            Stored((OwnGet, 2), (OldGet, 1))
        );
        return registry;
    }

    private static IReadOnlyDictionary<string, StoredMachineDocument> Empty() =>
        new Dictionary<string, StoredMachineDocument>();

    private static IReadOnlyDictionary<string, StoredMachineDocument> Stored(
        params (string Id, int Version)[] rows
    ) => rows.ToDictionary(r => r.Id, r => new StoredMachineDocument(r.Version, true));

    private static Func<
        CancellationToken,
        Task<IReadOnlyDictionary<string, StoredMachineDocument>>
    > Store(Func<IReadOnlyDictionary<string, StoredMachineDocument>> read, Action? onRead = null) =>
        _ =>
        {
            onRead?.Invoke();
            return Task.FromResult(read());
        };

    [Test]
    public async Task A_stored_active_id_of_a_machine_this_node_does_not_serve_is_server_outdated()
    {
        var ledger = Id("ledger", "start", NewHash);
        var registry = Registry();
        registry.Publish(
            [new MachineDocument(OwnGet, "wizard", "get", "query { }", OwnHash)],
            new Dictionary<string, int> { ["wizard"] = 2 },
            Stored((OwnGet, 2), (ledger, 1))
        );

        (await registry.ClassifyAsync(ledger, Store(Empty), CancellationToken.None))
            .Should()
            .Be(
                MachineDocumentVerdict.ServerOutdated,
                "a node that serves the machine registered the id and it is still active, so the client retries, "
                    + "per "
                    + Adr
            );
    }

    [Test]
    public async Task A_stored_client_document_of_any_version_is_run_once_it_passes_and_a_collected_one_is_outdated()
    {
        var own = ClientDocuments.IdOf("OrderCard", "query OrderCard { b }");
        var older = ClientDocuments.IdOf("OrderCard", "query OrderCard { a }");
        var collected = ClientDocuments.IdOf("OrderCard", "query OrderCard { c }");
        var registry = Registry();
        registry.Publish(
            [
                new ClientDocument(
                    own,
                    "query OrderCard { b }",
                    "OrderCard",
                    "client document 'OrderCard'"
                ),
            ],
            new Dictionary<string, int> { ["trax_client_OrderCard"] = 3 },
            new Dictionary<string, StoredMachineDocument>
            {
                [own] = new(3, true),
                [older] = new(2, true),
                [collected] = new(1, false),
            }
        );

        (await registry.ClassifyAsync(older, Store(Empty), CancellationToken.None))
            .Should()
            .Be(
                MachineDocumentVerdict.Stored,
                "an older client document keeps running through a frontend's deploy, per "
                    + "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md"
            );
        (await registry.ClassifyAsync(collected, Store(Empty), CancellationToken.None))
            .Should()
            .Be(MachineDocumentVerdict.ClientOutdated);

        registry.IsRunnableStored(older).Should().BeFalse("nothing checked its text yet");
        registry.RecordStoredCheck(older, passes: true);
        registry.IsRunnableStored(older).Should().BeTrue();
        registry.RecordStoredCheck(older, passes: false);
        registry
            .IsRunnableStored(older)
            .Should()
            .BeFalse("a stored text this node's checks refuse never runs here");
        registry.RecordStoredCheck(collected, passes: true);
        registry.IsRunnableStored(collected).Should().BeFalse("a collected document never runs");
    }

    [Test]
    public async Task A_collected_id_of_a_machine_this_node_does_not_serve_is_client_outdated()
    {
        var ledger = Id("ledger", "start", OldHash);
        var registry = Registry();
        registry.Publish(
            [new MachineDocument(OwnGet, "wizard", "get", "query { }", OwnHash)],
            new Dictionary<string, int> { ["wizard"] = 2 },
            new Dictionary<string, StoredMachineDocument>
            {
                [OwnGet] = new(2, true),
                [ledger] = new(1, false),
            }
        );

        (await registry.ClassifyAsync(ledger, Store(Empty), CancellationToken.None))
            .Should()
            .Be(
                MachineDocumentVerdict.ClientOutdated,
                "no node registering serves the machine any more, so retrying would never reach one; the client "
                    + "reloads, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_unseen_id_of_a_machine_this_node_does_not_serve_is_client_outdated_once_a_later_read_lacks_it()
    {
        var registry = Registry(TimeSpan.FromMilliseconds(50));
        var made = Id("ledger", "start", NewHash);
        var store = Store(() => Stored((OwnGet, 2), (OldGet, 1)));

        (await registry.ClassifyAsync(made, store, CancellationToken.None))
            .Should()
            .Be(
                MachineDocumentVerdict.ServerOutdated,
                "until the store is read again the id may be a newer node's machine"
            );

        var verdict = await PollAsync(registry, made, store);

        verdict
            .Should()
            .Be(
                MachineDocumentVerdict.ClientOutdated,
                "a read made after the id was first asked for does not hold it, so no node registered it, per "
                    + Adr
            );
    }

    [Test]
    public async Task A_flood_of_unseen_ids_forgets_the_oldest_and_keeps_an_id_asked_for_after_most_of_it()
    {
        // Never refreshed within the test: every id stays remembered until the bound pushes it out.
        var registry = Registry(TimeSpan.FromHours(1));
        var store = Store(Empty);
        string Made(string tag, int i) =>
            Id("wizard", "get", $"{tag}{i:x8}".PadLeft(MachineDocuments.IrHashLength, 'f'));

        for (var i = 0; i < MachineDocumentRegistry.MissCapacity - 10; i++)
            await registry.ClassifyAsync(Made("a", i), store, CancellationToken.None);
        var stale = Made("5", 0);
        await registry.ClassifyAsync(stale, store, CancellationToken.None);
        for (var i = 0; i < 20; i++)
            await registry.ClassifyAsync(Made("b", i), store, CancellationToken.None);

        registry
            .IsRemembered(stale)
            .Should()
            .BeTrue(
                "the bound forgets the oldest ids first, so a flood does not wipe an id asked for after most of "
                    + "it, which then turns client-outdated after the next read, per "
                    + Adr
            );
        registry.IsRemembered(Made("a", 0)).Should().BeFalse("the oldest are forgotten first");
        registry.IsRemembered(Made("b", 19)).Should().BeTrue();
        registry
            .RememberedMisses.Should()
            .BeLessThanOrEqualTo(MachineDocumentRegistry.MissCapacity);
    }

    private static async Task<MachineDocumentVerdict> PollAsync(
        MachineDocumentRegistry registry,
        string id,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, StoredMachineDocument>>> store
    )
    {
        var verdict = MachineDocumentVerdict.ServerOutdated;
        var deadline = Stopwatch.StartNew();
        while (
            verdict == MachineDocumentVerdict.ServerOutdated
            && deadline.Elapsed < TimeSpan.FromSeconds(10)
        )
        {
            // allowed-delay: polls the answer until the background read has run.
            await Task.Delay(20);
            verdict = await registry.ClassifyAsync(id, store, CancellationToken.None);
        }
        return verdict;
    }

    [Test]
    public async Task A_malformed_id_of_a_machine_this_node_does_not_serve_is_client_outdated()
    {
        var verdict = await Registry()
            .ClassifyAsync(
                $"{MachineDocuments.ReservedIdPrefix}ledger_start_nothex",
                Store(() => Empty()),
                CancellationToken.None
            );

        verdict.Should().Be(MachineDocumentVerdict.ClientOutdated, "no version generated it");
    }

    [Test]
    public async Task An_older_stored_hash_of_a_machine_this_node_serves_is_client_outdated()
    {
        var verdict = await Registry()
            .ClassifyAsync(OldGet, Store(() => Empty()), CancellationToken.None);

        verdict.Should().Be(MachineDocumentVerdict.ClientOutdated, "per " + Adr);
    }

    [Test]
    public async Task An_unseen_id_is_answered_at_once_and_concurrent_misses_never_wait_on_each_other()
    {
        var registry = Registry(TimeSpan.FromSeconds(30));
        var reads = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<
            CancellationToken,
            Task<IReadOnlyDictionary<string, StoredMachineDocument>>
        > slowStore = async _ =>
        {
            Interlocked.Increment(ref reads);
            await release.Task;
            return Empty();
        };

        var all = Task.WhenAll(
            Enumerable
                .Range(0, 200)
                .Select(i =>
                    registry
                        .ClassifyAsync(
                            Id(
                                "wizard",
                                "get",
                                $"{i:x4}".PadLeft(MachineDocuments.IrHashLength, 'd')
                            ),
                            slowStore,
                            CancellationToken.None
                        )
                        .AsTask()
                )
        );
        // Bounded: misses that queue behind the read would wait for it forever, since it is released only after.
        // allowed-delay: a bound, not a wait; the misses either finish well inside it or queue forever.
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(5))) == all;
        release.SetResult();
        finished
            .Should()
            .BeTrue(
                "a miss is answered from the last read of the store, never queued behind a read, per "
                    + Adr
            );
        var verdicts = await all;

        verdicts
            .Should()
            .OnlyContain(
                v => v == MachineDocumentVerdict.ServerOutdated,
                "an id the node has not seen may be a newer node's, so until the store is read again the client "
                    + "retries"
            );
        reads.Should().BeLessThanOrEqualTo(1, "the store is read at most once per interval");
    }

    [Test]
    public async Task An_id_the_store_still_lacks_after_a_later_read_is_client_outdated()
    {
        var registry = Registry(TimeSpan.FromMilliseconds(50));
        var made = Id("wizard", "get", NewHash);
        var reads = 0;
        var store = Store(
            () => Stored((OwnGet, 2), (OldGet, 1)),
            () => Interlocked.Increment(ref reads)
        );

        (await registry.ClassifyAsync(made, store, CancellationToken.None))
            .Should()
            .Be(MachineDocumentVerdict.ServerOutdated, "the store has not been read since");

        var verdict = MachineDocumentVerdict.ServerOutdated;
        var deadline = Stopwatch.StartNew();
        while (
            verdict == MachineDocumentVerdict.ServerOutdated
            && deadline.Elapsed < TimeSpan.FromSeconds(10)
        )
        {
            // allowed-delay: polls the answer until the background read has run.
            await Task.Delay(20);
            verdict = await registry.ClassifyAsync(made, store, CancellationToken.None);
        }

        verdict
            .Should()
            .Be(
                MachineDocumentVerdict.ClientOutdated,
                "a read made after the id was first asked for does not hold it, so no version generated it, or it "
                    + "was collected"
            );
        reads.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task An_id_a_newer_node_registered_is_server_outdated_once_the_store_is_read()
    {
        var registry = Registry(TimeSpan.FromMilliseconds(50));
        var newer = Id("wizard", "get", NewHash);
        var store = Store(() => Stored((OwnGet, 2), (OldGet, 1), (newer, 3)));

        await registry.ClassifyAsync(newer, store, CancellationToken.None);
        var deadline = Stopwatch.StartNew();
        while (
            registry.IsNewer("wizard", NewHash) != true
            && deadline.Elapsed < TimeSpan.FromSeconds(10)
        )
        {
            (await registry.ClassifyAsync(newer, store, CancellationToken.None))
                .Should()
                .Be(
                    MachineDocumentVerdict.ServerOutdated,
                    "a newer version is retryable, per " + Adr
                );
            // allowed-delay: polls the answer until the background read has run.
            await Task.Delay(20);
        }
        registry.IsNewer("wizard", NewHash).Should().BeTrue("the background read ordered it");
        (await registry.ClassifyAsync(newer, store, CancellationToken.None))
            .Should()
            .Be(
                MachineDocumentVerdict.ServerOutdated,
                "once read, a newer version stays retryable, per " + Adr
            );
    }

    [Test]
    public async Task Misses_read_the_store_at_most_once_per_interval()
    {
        var registry = Registry(TimeSpan.FromMilliseconds(300));
        var reads = 0;
        var store = Store(() => Empty(), () => Interlocked.Increment(ref reads));

        var clock = Stopwatch.StartNew();
        var i = 0;
        while (clock.Elapsed < TimeSpan.FromSeconds(1.5))
        {
            await registry.ClassifyAsync(
                Id("wizard", "get", $"{i++:x8}".PadLeft(MachineDocuments.IrHashLength, 'e')),
                store,
                CancellationToken.None
            );
            // measuring-interval: paces the flood of misses over the measured window.
            await Task.Delay(5);
        }

        reads.Should().BeInRange(1, 7, "a flood of misses reads the store about once per interval");
    }
}
