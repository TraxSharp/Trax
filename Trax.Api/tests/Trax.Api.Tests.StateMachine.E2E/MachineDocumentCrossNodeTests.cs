using AwesomeAssertions;
using CsCheck;
using HotChocolate.Configuration;
using HotChocolate.Types.Descriptors.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The machine documents across nodes and deploys: nodes starting together register each document once; the same
/// schema yields the same text in every process; a document's id changes with its text even when the IR does not;
/// and in a rolling deploy each node serves only its own hashes, answering an older one <c>CLIENT_OUTDATED</c> and a
/// newer one a retryable <c>SERVER_OUTDATED</c>, never deactivating an older IR's documents at startup and collecting
/// them only after a grace period. Each test runs its own nodes over a fresh database, with
/// <see cref="ShelfMachine"/> built as one version on one node and another on the next.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineDocumentCrossNodeTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_document_nodes";

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
    public async Task Two_nodes_starting_together_register_each_document_once_and_both_start()
    {
        // The first node migrates the empty database; its documents are then removed, as on a database that never
        // held them.
        await ShelfHosts.StopAsync(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "primer"));
        await E2ESql.ExecuteAsync(
            Database,
            "DELETE FROM trax.persisted_operation WHERE starts_with(id, @prefix); "
                + "DELETE FROM trax.persisted_operation_history WHERE starts_with(id, @prefix)",
            ("prefix", MachineDocuments.ReservedIdPrefix)
        );

        using var together = new Barrier(2);
        var starting = new[] { "together_a", "together_b" }.Select(name =>
            Task.Run(() =>
            {
                together.SignalAndWait(TimeSpan.FromSeconds(30));
                return ShelfHosts.StartAsync(Database, ShelfShape.V1, name);
            })
        );
        var nodes = await Task.WhenAll(starting);
        _hosts.AddRange(nodes);

        var documents = (await MachineDocumentOps.MachineDocumentsAsync(Database))
            .Where(d => d.TenantKey == "")
            .ToList();
        documents
            .Select(d => d.Operation(ShelfMachine.MachineId))
            .Where(op => op is not null && op != MachineDocumentOps.MachineChanged)
            .Should()
            .BeEquivalentTo(
                ["start", "discard", "get", .. ShelfMachine.ClientEventFields],
                "both nodes registered the shelf's documents, each once, per " + Adr
            );
        documents.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        (await MachineDocumentOps.HistoryRowsAsync(Database))
            .Should()
            .Be(documents.Count, "each document was written once, by whichever node won");
    }

    [Test]
    public async Task Document_text_is_identical_across_processes()
    {
        var nodes = await TwoNodes.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine), typeof(MemoMachine), typeof(ShelfMachine)],
                GraphQL = MachineDocumentOps.Persisted(),
            }
        );
        try
        {
            var schemaA = (await nodes.A.ExecutorAsync()).Schema;
            var schemaB = (await nodes.B.ExecutorAsync()).Schema;
            var machinesA = nodes.A.Services.GetServices<IMachine>().ToList();
            var machinesB = nodes.B.Services.GetServices<IMachine>().ToList();
            var exposed = new[]
            {
                typeof(ServedWizardMachine),
                typeof(ServedMemoMachine),
                typeof(ServedShelfMachine),
            };

            Gen.Shuffle(exposed)
                .Select(Gen.Int[1, exposed.Length], (order, count) => order.Take(count).ToArray())
                .Sample(types =>
                {
                    var onA = MachineDocuments.Generate(
                        schemaA,
                        types.Select(t => machinesA.Single(m => m.GetType() == t))
                    );
                    var onB = MachineDocuments.Generate(
                        schemaB,
                        types.Reverse().Select(t => machinesB.Single(m => m.GetType() == t))
                    );

                    onA.Should().NotBeEmpty();
                    onA.OrderBy(d => d.Id, StringComparer.Ordinal)
                        .Select(d => (d.Id, d.Text))
                        .Should()
                        .Equal(
                            onB.OrderBy(d => d.Id, StringComparer.Ordinal)
                                .Select(d => (d.Id, d.Text)),
                            "two processes building one schema generate the same documents byte for byte, "
                                + "whatever order the machines come in, per "
                                + Adr
                        );
                });
        }
        finally
        {
            await nodes.DisposeAsync();
            await E2EHost.RecreateDatabaseAsync(Database);
        }
    }

    [Test]
    public async Task A_graphql_naming_change_without_an_ir_change_yields_a_new_id()
    {
        _hosts.Add(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "naming_a"));
        var before = await MachineDocumentOps.MachineDocumentsAsync(
            Database,
            WizardMachine.MachineId
        );
        before.Should().NotBeEmpty("the first node registered the wizard's documents, per " + Adr);

        _hosts.Add(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "naming_b",
                new E2EHostOptions
                {
                    GraphQL = graphql =>
                        MachineDocumentOps
                            .Persisted()(graphql)
                            .ConfigureSchema(schema =>
                                schema.TryAddTypeInterceptor<RenameDescribeInput>()
                            ),
                }
            )
        );
        var after = await MachineDocumentOps.MachineDocumentsAsync(
            Database,
            WizardMachine.MachineId
        );

        after
            .Where(d => d.Operation(WizardMachine.MachineId) == "describe")
            .Select(d => d.Id)
            .Should()
            .HaveCount(
                2,
                "the machine's IR did not change, but the describe document's text names the renamed input type, "
                    + "and the id covers the text, per "
                    + Adr
            );
        after
            .Where(d => d.Operation(WizardMachine.MachineId) == "get")
            .Should()
            .ContainSingle("a document whose text is unchanged keeps its id");
    }

    [Test]
    public async Task After_an_ir_change_the_old_id_answers_client_outdated_and_the_new_one_works()
    {
        var a = await ShelfHosts.StartAsync(Database, ShelfShape.V1, "deploy_a");
        var v1 = await ShelfIdsAsync();
        await ShelfHosts.StopAsync(a);
        var b = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "deploy_b"));
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();
        v1.Should().NotBeEmpty("the first version's documents were registered, per " + Adr);
        v2.Should().NotBeEmpty("the second version registered its own");

        var id = await b.StartShelfAsync(E2EHost.AliceKey);
        using var old = await b.PostPersistedAsync(Pick(v1, "get"), new { id }, E2EHost.AliceKey);
        using var current = await b.PostPersistedAsync(
            Pick(v2, "get"),
            new { id },
            E2EHost.AliceKey
        );

        MachineDocumentOps
            .Codes(old)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "the old version's id tells the client to reload, per " + Adr
            );
        MachineDocumentOps
            .Succeeded(current, ShelfMachine.MachineId, "get")
            .Should()
            .BeTrue(MachineDocumentOps.Show(current));
    }

    [Test]
    public async Task A_node_serves_machine_documents_only_for_its_own_hashes()
    {
        var (a, b, v1, _) = await RollingDeployAsync();
        var id = await a.StartShelfAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, ShelfMachine.MachineId, id);

        using var answer = await b.PostPersistedAsync(
            Pick(v1, "file"),
            new { id, requestId = WizardOps.NewRequestId() },
            E2EHost.AliceKey
        );

        MachineDocumentOps
            .Codes(answer)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "an older version's id, still stored and active, is not this node's to run, per "
                    + Adr
            );
        (await E2ESql.DraftAsync(Database, ShelfMachine.MachineId, id))
            .Should()
            .Be(before, "the refused document ran nothing");
    }

    [Test]
    public async Task A_newer_hash_on_an_older_node_is_server_outdated_and_retryable()
    {
        var (a, b, _, v2) = await RollingDeployAsync();
        var id = await b.StartShelfAsync(E2EHost.AliceKey);

        using var onOld = await a.PostPersistedAsync(Pick(v2, "get"), new { id }, E2EHost.AliceKey);
        using var onNew = await b.PostPersistedAsync(Pick(v2, "get"), new { id }, E2EHost.AliceKey);

        MachineDocumentOps
            .Codes(onOld)
            .Should()
            .Contain(
                RefusalCode.ServerOutdated.ToWire(),
                "a node that does not yet run the newer version says so, and the client retries, per "
                    + Adr
            )
            .And.NotContain(
                RefusalCode.ClientOutdated.ToWire(),
                "the client is not the one behind"
            );
        MachineDocumentOps
            .Succeeded(onNew, ShelfMachine.MachineId, "get")
            .Should()
            .BeTrue("the retry reaches a node that runs it: " + MachineDocumentOps.Show(onNew));
    }

    [Test]
    public async Task A_client_bouncing_between_old_and_new_nodes_never_gets_client_outdated_for_a_newer_hash()
    {
        var (a, b, _, v2) = await RollingDeployAsync();
        var id = await b.StartShelfAsync(E2EHost.AliceKey);
        var get = Pick(v2, "get");

        var answers = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            using var answer = await (i % 2 == 0 ? a : b).PostPersistedAsync(
                get,
                new { id },
                E2EHost.AliceKey
            );
            answers.Add(
                MachineDocumentOps.Succeeded(answer, ShelfMachine.MachineId, "get")
                    ? "ok"
                    : string.Join(",", MachineDocumentOps.Codes(answer))
            );
        }

        answers
            .Should()
            .OnlyContain(
                a => a == "ok" || a == RefusalCode.ServerOutdated.ToWire(),
                "a newer id is either served or answered server-outdated, never client-outdated, per "
                    + Adr
            );
        answers.Should().Contain("ok");
    }

    [Test]
    public async Task Startup_never_deactivates_an_older_irs_documents()
    {
        var (_, _, v1, _) = await RollingDeployAsync();

        (await MachineDocumentOps.MachineDocumentsAsync(Database, ShelfMachine.MachineId))
            .Where(d => v1.Contains(d.Id))
            .Should()
            .HaveCount(v1.Count)
            .And.OnlyContain(
                d => d.IsActive,
                "a node still serving the older version needs its documents, per " + Adr
            );
    }

    [Test]
    public async Task Old_ids_are_collected_only_after_the_grace_period()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var withClock = new E2EHostOptions
        {
            Services = services => services.AddSingleton<TimeProvider>(clock),
        };
        await ShelfHosts.StopAsync(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "grace_v1")
        );
        var v1 = await ShelfIdsAsync();
        v1.Should().NotBeEmpty("the first version's documents were registered, per " + Adr);
        Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "grace_v2", withClock));

        clock.Advance(TimeSpan.FromMinutes(1));
        Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "grace_v2b", withClock));
        (await ActiveAsync(v1))
            .Should()
            .Be(v1.Count, "a minute after the deploy an older client may still be running");

        clock.Advance(TimeSpan.FromDays(400));
        Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "grace_v2c", withClock));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(v1) == 0))
            .Should()
            .BeTrue(
                "past the grace period the older version's documents are collected, per " + Adr
            );
        (await ShelfIdsAsync()).Except(v1).Should().NotBeEmpty("the current version's stay");
    }

    [Test]
    public async Task A_running_node_collects_old_ids_after_the_grace_period_without_a_restart()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var running = new E2EHostOptions
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
        await ShelfHosts.StopAsync(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "timer_collect_v1")
        );
        var v1 = await ShelfIdsAsync();
        v1.Should().NotBeEmpty("the first version's documents were registered, per " + Adr);
        Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "timer_collect_v2", running));

        // negative-wait: several collection intervals pass inside the grace period, and nothing may be collected.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await ActiveAsync(v1))
            .Should()
            .Be(v1.Count, "within the grace period an older client may still be running");

        clock.Advance(TimeSpan.FromDays(400));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(v1) == 0))
            .Should()
            .BeTrue(
                "a running node collects the older version's documents once the grace period has passed, "
                    + "without waiting for a restart, per "
                    + Adr
            );
        (await ShelfIdsAsync()).Except(v1).Should().NotBeEmpty("the current version's stay");
    }

    [Test]
    public async Task A_machine_exposed_only_on_a_newer_node_is_server_outdated_on_an_older_one()
    {
        // The older node was built before the shelf was exposed: it serves the wizard alone.
        var older = Track(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "exposed_older",
                new E2EHostOptions { Served = [typeof(WizardMachine)] }
            )
        );
        var newer = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "exposed_newer"));
        var shelf = await ShelfIdsAsync();
        shelf.Should().NotBeEmpty("the newer node registered the shelf's documents, per " + Adr);
        var id = await newer.StartShelfAsync(E2EHost.AliceKey);

        using var onOlder = await older.PostPersistedAsync(
            Pick(shelf, "get"),
            new { id },
            E2EHost.AliceKey
        );
        using var start = await older.PostPersistedAsync(
            Pick(shelf, "start"),
            new { input = new { label = "Atlas" }, requestId = WizardOps.NewRequestId() },
            E2EHost.AliceKey
        );

        foreach (var answer in new[] { onOlder, start })
            MachineDocumentOps
                .Codes(answer)
                .Should()
                .Contain(
                    RefusalCode.ServerOutdated.ToWire(),
                    "a node that does not serve the machine yet tells the client to retry, which may reach a "
                        + "node that does, per "
                        + Adr
                )
                .And.NotContain(
                    RefusalCode.ClientOutdated.ToWire(),
                    "the client is not the one behind"
                );
    }

    [Test]
    public async Task A_machine_a_newer_deploy_stopped_exposing_is_client_outdated_once_its_documents_are_collected()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await ShelfHosts.StopAsync(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "dropped_older")
        );
        var shelf = await ShelfIdsAsync();
        shelf.Should().NotBeEmpty("the older deploy registered the shelf's documents, per " + Adr);

        // The newer deploy serves the wizard alone.
        var newer = Track(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "dropped_newer",
                Collecting(clock) with
                {
                    Served = [typeof(WizardMachine)],
                }
            )
        );
        var get = Pick(shelf, "get");

        // negative-wait: several collection intervals pass inside the grace period, and nothing may be collected.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await ActiveAsync(shelf))
            .Should()
            .Be(shelf.Count, "within the grace period an older node may still serve the shelf");
        using (var during = await newer.PostPersistedAsync(get, new { id = Guid.NewGuid() }))
            MachineDocumentOps
                .Codes(during)
                .Should()
                .Contain(
                    RefusalCode.ServerOutdated.ToWire(),
                    "while an older node may still serve the machine the client retries, per " + Adr
                );

        clock.Advance(TimeSpan.FromDays(400));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(shelf) == 0))
            .Should()
            .BeTrue(
                "past the grace period no registering node has served the machine, so its documents are "
                    + "collected, per "
                    + Adr
            );
        using var after = await newer.PostPersistedAsync(get, new { id = Guid.NewGuid() });
        MachineDocumentOps
            .Codes(after)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "once the store holds no active id for the machine, retrying can never reach a node that serves "
                    + "it, so the client reloads, per "
                    + Adr
            );
    }

    [Test]
    public async Task A_deploy_that_serves_no_machine_still_collects_the_old_documents_and_old_ids_turn_client_outdated()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await ShelfHosts.StopAsync(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "last_served")
        );
        var shelf = await ShelfIdsAsync();
        shelf.Should().NotBeEmpty("the older deploy registered the shelf's documents, per " + Adr);

        // The newer deploy marks no machine [TraxMachine]: it registers its machines, unmarked, and serves none.
        var newer = Track(
            await E2EHost.StartAsync(
                Database,
                new CountingCharge(),
                Collecting(clock) with
                {
                    ApplicationName = "serves_none",
                    GraphQL = MachineDocumentOps.Persisted(),
                }
            )
        );
        var get = Pick(shelf, "get");

        using (var during = await newer.PostPersistedAsync(get, new { id = Guid.NewGuid() }))
            MachineDocumentOps
                .Codes(during)
                .Should()
                .Contain(
                    RefusalCode.ServerOutdated.ToWire(),
                    "while an older node may still serve the machine the client retries, per " + Adr
                );

        clock.Advance(TimeSpan.FromDays(400));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(shelf) == 0))
            .Should()
            .BeTrue(
                "a node that serves no machine still registers its empty set of documents, so past the grace "
                    + "period the documents of a machine no node serves are collected, per "
                    + Adr
            );
        using var after = await newer.PostPersistedAsync(get, new { id = Guid.NewGuid() });
        MachineDocumentOps
            .Codes(after)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "once no node serves the machine and its documents are collected, the client reloads, per "
                    + Adr
            );
    }

    [Test]
    public async Task A_machine_a_newer_node_serves_is_never_collected_by_an_older_node_that_does_not()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        // The older node was built before the shelf was exposed; the newer one serves it.
        var older = Track(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "unexposed_older",
                Collecting(clock) with
                {
                    Served = [typeof(WizardMachine)],
                }
            )
        );
        Track(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "unexposed_newer",
                Collecting(clock)
            )
        );
        var shelf = await ShelfIdsAsync();
        shelf.Should().NotBeEmpty();

        clock.Advance(TimeSpan.FromDays(400));
        // negative-wait: both nodes register several times past the grace period, and nothing may be collected.
        await Task.Delay(TimeSpan.FromSeconds(2));

        (await ActiveAsync(shelf))
            .Should()
            .Be(
                shelf.Count,
                "the newer node clears the older one's unexposed mark every time it registers, so a machine a "
                    + "running node serves is never collected, per "
                    + Adr
            );
        (await DeactivationsAsync(shelf)).Should().Be(0);
        older.Should().NotBeNull();
    }

    [Test]
    public async Task An_older_node_still_registering_keeps_its_documents_past_the_grace_period()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var older = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "lingering_v1", Collecting(clock))
        );
        var v1 = await ShelfIdsAsync();
        var newer = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V2, "lingering_v2", Collecting(clock))
        );
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();

        await MachineDocumentOps.PassTheGracePeriodRegisteringAsync(
            Database,
            clock,
            [.. v1, .. v2]
        );

        (await DeactivationsAsync(v1))
            .Should()
            .Be(
                0,
                "a node still running an older version registers its documents, so no newer node collects "
                    + "them, per "
                    + Adr
            );
        (await ActiveAsync(v1)).Should().Be(v1.Count);
        var id = await older.StartShelfAsync(E2EHost.AliceKey);
        using var onOlder = await older.PostPersistedAsync(
            Pick(v1, "get"),
            new { id },
            E2EHost.AliceKey
        );
        MachineDocumentOps
            .Succeeded(onOlder, ShelfMachine.MachineId, "get")
            .Should()
            .BeTrue("the older node's clients still run: " + MachineDocumentOps.Show(onOlder));
        using var onNewer = await newer.PostPersistedAsync(
            Pick(v1, "get"),
            new { id },
            E2EHost.AliceKey
        );
        MachineDocumentOps
            .Codes(onNewer)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "an older id sent to a newer node still tells the client to reload"
            );
    }

    [Test]
    public async Task An_older_version_no_node_registers_is_collected_after_the_grace_period()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var older = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V1, "gone_v1", Collecting(clock))
        );
        var v1 = await ShelfIdsAsync();
        var newer = Track(
            await ShelfHosts.StartAsync(Database, ShelfShape.V2, "gone_v2", Collecting(clock))
        );
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();
        // The older node is stopped: nothing registers its documents from here on.
        _hosts.Remove(older);
        await ShelfHosts.StopAsync(older);

        clock.Advance(TimeSpan.FromDays(1));
        // negative-wait: the newer node registers several times inside the grace period, and nothing may be collected.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await ActiveAsync(v1))
            .Should()
            .Be(v1.Count, "within the grace period a node of the older version may still run");

        clock.Advance(TimeSpan.FromDays(400));
        (await E2EWait.UntilAsync(async () => await ActiveAsync(v1) == 0))
            .Should()
            .BeTrue(
                "no node has registered the older version for the grace period, so it is collected, per "
                    + Adr
            );
        (await ActiveAsync(v2)).Should().Be(v2.Count, "the newer node's own stay");
        var id = await newer.StartShelfAsync(E2EHost.AliceKey);
        using var old = await newer.PostPersistedAsync(
            Pick(v1, "get"),
            new { id },
            E2EHost.AliceKey
        );
        MachineDocumentOps
            .Codes(old)
            .Should()
            .Contain(
                RefusalCode.ClientOutdated.ToWire(),
                "a collected older id, with its own node gone, tells the client to reload, per "
                    + Adr
            );

        // A node rolled back to the older version brings them back when it registers, and keeps them.
        Track(
            await ShelfHosts.StartAsync(
                Database,
                ShelfShape.V1,
                "rolled_back_v1",
                Collecting(clock)
            )
        );
        // negative-wait: both nodes register several times; the newer one may not collect what the older registers.
        await Task.Delay(TimeSpan.FromSeconds(1));
        (await ActiveAsync(v1))
            .Should()
            .Be(v1.Count, "a node on the older version serves them again");
    }

    // A host on the shared clock that registers its machine documents again every 200 ms.
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

    private static Task<long> DeactivationsAsync(IReadOnlyList<string> ids) =>
        HistoryOfAsync(ids, PersistedOperationChangeType.Deactivate);

    private static Task<long> HistoryOfAsync(IReadOnlyList<string> ids, string changeType) =>
        E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM trax.persisted_operation_history WHERE id = ANY(@ids) AND change_type = @type",
            ("ids", ids.ToArray()),
            ("type", changeType)
        );

    [Test]
    public async Task Adding_a_timer_changes_the_document_ids()
    {
        var a = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "timer_a"));
        var v1 = await ShelfIdsAsync();
        var id = await a.StartShelfAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(a, E2EHost.AliceKey);
        await ws.SubscribeAsync("shelf", MachineChangedOps.Subscribe(id, ShelfMachine.MachineId));
        await ws.NextChangeAsync("shelf");

        var b = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2WithTimer, "timer_b"));
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();

        v1.Should().NotBeEmpty();
        v2.Select(Operation)
            .Should()
            .BeEquivalentTo(
                v1.Select(Operation),
                "a timer changes the IR, so every document of the machine gets a new id, per " + Adr
            );
        using var old = await b.PostPersistedAsync(Pick(v1, "get"), new { id }, E2EHost.AliceKey);
        MachineDocumentOps.Codes(old).Should().Contain(RefusalCode.ClientOutdated.ToWire());

        using (await b.SendShelfEventAsync(E2EHost.AliceKey, "file", id)) { }
        var events = await ws.ReadUntilEndAsync("shelf");
        events[^1]
            .ProblemCode.Should()
            .Be(
                RefusalCode.ClientOutdated.ToWire(),
                "a row the newer version wrote ends the older node's stream the same way"
            );
    }

    // Node A serves version 1 and node B version 2, both running, over one database.
    private async Task<(
        IHost A,
        IHost B,
        IReadOnlyList<string> V1,
        IReadOnlyList<string> V2
    )> RollingDeployAsync()
    {
        var a = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V1, "rolling_a"));
        var v1 = await ShelfIdsAsync();
        var b = Track(await ShelfHosts.StartAsync(Database, ShelfShape.V2, "rolling_b"));
        var v2 = (await ShelfIdsAsync()).Except(v1).ToList();
        v1.Should().NotBeEmpty("node A registered the first version's documents, per " + Adr);
        v2.Should().NotBeEmpty("node B registered the second version's");
        return (a, b, v1, v2);
    }

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

    private static string? Operation(string id) =>
        new MachineDocumentOps.StoredDocument("", id, "", true).Operation(ShelfMachine.MachineId);

    private static string Pick(IReadOnlyList<string> ids, string operation) =>
        ids.Single(i => Operation(i) == operation);

    /// <summary>
    /// Renames the GraphQL input type of the wizard's describe event, which leaves the IR alone. Before the name is
    /// completed: HotChocolate has fixed a type's name by the time <c>OnAfterCompleteName</c> runs.
    /// </summary>
    private sealed class RenameDescribeInput : TypeInterceptor
    {
        public override void OnBeforeCompleteName(
            ITypeCompletionContext completionContext,
            TypeSystemConfiguration configuration
        )
        {
            if (
                configuration is InputObjectTypeConfiguration input
                && input.RuntimeType == typeof(WizardDetailsInput)
            )
                input.Name += "Renamed";
        }
    }
}
