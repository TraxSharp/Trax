using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The versions of a generated document's slot in the cases a rolling deploy does not cover: a rollback, which leaves
/// a newer version no node registers; two hosts on one database that register two versions of one slot for good; and
/// a row uploaded under a reserved prefix before the prefix was reserved, which no host generated.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineDocumentStorageTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_document_storage";

    private readonly List<IHost> _hosts = [];

    [SetUp]
    public Task SetUp() => E2EHost.RecreateDatabaseAsync(Database);

    [TearDown]
    public async Task TearDown()
    {
        foreach (var host in _hosts)
            await ShelfHosts.StopAsync(host);
        _hosts.Clear();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task A_newer_shape_no_node_registers_turns_client_outdated_after_grace()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await ShelfHosts.StopAsync(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "rb_v1"));
        var v1 = await ShelfIdsAsync();
        await ShelfHosts.StopAsync(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "rb_v2"));
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();
        v2.Should().NotBeEmpty("the newer deploy registered its documents");

        // The deploy is rolled back: only nodes of the first shape run from here on.
        var rolledBack = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "rb_back", Collecting(clock))
        );
        var get = Pick(v2, "get");
        using (var during = await rolledBack.PostPersistedAsync(get, new { id = Guid.NewGuid() }))
            MachineDocumentOps
                .Codes(during)
                .Should()
                .Contain(
                    RefusalCode.ServerOutdated.ToWire(),
                    "within the grace period a node of the newer shape may still be running"
                );

        clock.Advance(TimeSpan.FromDays(400));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(v2) == 0))
            .Should()
            .BeTrue(
                "no node has registered the newer shape for the grace period, so it is collected, per "
                    + Adr
            );
        (
            await E2EWait.UntilAsync(async () =>
            {
                using var after = await rolledBack.PostPersistedAsync(
                    get,
                    new { id = Guid.NewGuid() }
                );
                return MachineDocumentOps
                    .Codes(after)
                    .Contains(RefusalCode.ClientOutdated.ToWire());
            })
        ).Should().BeTrue("a collected newer version is served by no node, so its clients reload instead of retrying " + "forever, per " + Adr);
        (await ActiveAsync(v1)).Should().Be(v1.Count, "the rolled-back shape stays");
    }

    [Test]
    public async Task Two_hosts_registering_one_slot_keep_both_documents_runnable()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var first = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "two_v1", Collecting(clock))
        );
        var v1 = await ShelfIdsAsync();
        var second = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V2, "two_v2", Collecting(clock))
        );
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();
        v2.Should().NotBeEmpty("the second host registered its own version of the slot");

        await MachineDocumentOps.PassTheGracePeriodRegisteringAsync(
            Database,
            clock,
            [.. v1, .. v2]
        );

        (await ActiveAsync(v1))
            .Should()
            .Be(v1.Count, "a host that still registers its documents keeps them, per " + Adr);
        (await ActiveAsync(v2)).Should().Be(v2.Count, "and so does the other");
        foreach (var (host, ids) in new[] { (first, v1), (second, v2) })
        {
            var id = await host.StartShelfAsync(E2EHost.AliceKey);
            using var get = await host.PostPersistedAsync(
                Pick(ids, "get"),
                new { id },
                E2EHost.AliceKey
            );
            MachineDocumentOps
                .Succeeded(get, ShelfMachine.MachineId, "get")
                .Should()
                .BeTrue(
                    "each host's clients still run its documents: " + MachineDocumentOps.Show(get)
                );
        }
    }

    [Test]
    public async Task A_preexisting_row_under_a_new_prefix_is_not_collected()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await ShelfHosts.StopAsync(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "pre_v1"));
        var v1 = await ShelfIdsAsync();

        // Rows uploaded before the prefixes were reserved: an id no generator writes, an id shaped like a generated
        // one whose body does not hash to it, and one in the shelf's own slot with a version at the top of the range.
        string[] uploaded =
        [
            "trax_train_mutation__legacy_v1",
            ProgressDocuments.ReservedIdPrefix
                + "query_"
                + new string('a', 64)
                + new string('b', 16),
            MachineDocuments.ReservedIdPrefix
                + "shelf_get_"
                + new string('c', 64)
                + new string('d', 16),
        ];
        foreach (var (id, version) in uploaded.Zip(new[] { 1, 1, int.MaxValue }))
            await E2ESql.ExecuteAsync(
                Database,
                "INSERT INTO trax.persisted_operation (tenant_key, id, operation_name, version, document, "
                    + "shape_fingerprint, is_active, created_at, updated_at) VALUES ('', @id, 'Legacy', @version, "
                    + "'query Legacy { __typename }', 'x', true, now() - interval '30 days', now() - interval '30 days')",
                ("id", id),
                ("version", version)
            );

        Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "pre_v2", Collecting(clock)));
        var v2 = (await ShelfIdsAsync()).Except(v1).Except(uploaded).ToList();
        v2.Should().NotBeEmpty();
        clock.Advance(TimeSpan.FromDays(400));
        // negative-wait: the host registers several times past the grace period, and leaves the uploaded rows alone.
        await Task.Delay(TimeSpan.FromSeconds(2));

        foreach (var id in uploaded)
            (
                await E2ESql.ScalarAsync<long>(
                    Database,
                    "SELECT count(*) FROM trax.persisted_operation WHERE id = @id AND is_active "
                        + "AND deprecation_reason IS NULL",
                    ("id", id)
                )
            )
                .Should()
                .Be(1, $"no host generated '{id}', so registration neither marks nor collects it");
        (
            await E2ESql.ScalarAsync<int>(
                Database,
                "SELECT min(version) FROM trax.persisted_operation WHERE id = ANY(@ids)",
                ("ids", v2.ToArray())
            )
        )
            .Should()
            .Be(
                2,
                "an uploaded row's version is not counted, so the next version is numbered after v1's"
            );
    }

    // A host on the shared clock that registers its documents again every 200 ms.
    private static E2EHostOptions Collecting(ManualClock clock) =>
        new()
        {
            Services = services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                var options = (PersistedOperationsOptions)
                    services
                        .Last(d => d.ServiceType == typeof(PersistedOperationsOptions))
                        .ImplementationInstance!;
                options.MachineDocumentCollectionInterval = TimeSpan.FromMilliseconds(200);
            },
        };

    private IHost Track(IHost host)
    {
        _hosts.Add(host);
        return host;
    }

    private static async Task<IReadOnlyList<string>> ShelfIdsAsync() =>
        (await MachineDocumentOps.MachineDocumentsAsync(Database, ShelfMachine.MachineId))
            .Where(d => d.Operation(ShelfMachine.MachineId) != MachineDocumentOps.MachineChanged)
            .Select(d => d.Id)
            .ToList();

    private static async Task<int> ActiveAsync(IReadOnlyList<string> ids) =>
        (await MachineDocumentOps.MachineDocumentsAsync(Database, ShelfMachine.MachineId)).Count(
            d => d.IsActive && ids.Contains(d.Id)
        );

    private static string Pick(IReadOnlyList<string> ids, string operation) =>
        ids.Single(i =>
            new MachineDocumentOps.StoredDocument("", i, "", true).Operation(ShelfMachine.MachineId)
            == operation
        );
}
