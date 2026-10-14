using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// An exposed machine's operations are persisted documents the host registers itself when it starts: one each for
/// <c>start</c>, <c>discard</c>, <c>get</c> and every client event, under the reserved id prefix, under the null
/// tenant, with an id that hashes the document's text with the machine's IR. A restart that changes nothing writes
/// nothing; a stored body that does not match its id is refused and repaired; an unknown id under the prefix costs
/// no database read when it is malformed and at most one read per interval when it is well formed, and is never
/// answered with a 5xx; a request that arrives while the host starts never pins a miss; the manifest lists every
/// document behind the operations gate; and a type no finite document can describe refuses startup.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineDocumentsE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_documents";

    /// <summary>A body nobody generated, written over a stored document by hand.</summary>
    private const string TamperedBody = "query Tampered { __typename }";

    private IHost _host = null!;
    private CountingDataContexts _reads = null!;

    /// <summary>The host's options: the wizard exposed, the operations gated, the documents persisted.</summary>
    private static E2EHostOptions Options(Action<IServiceCollection>? services = null) =>
        new()
        {
            Served = [typeof(WizardMachine)],
            Machines = o =>
            {
                // No sweep reads the database while a test counts reads.
                o.InvokeOutcomeSweepInterval = TimeSpan.FromHours(1);
                o.InvokeOutcomeSweepIntervalWhileListening = TimeSpan.FromHours(1);
            },
            ExposeOperations = true,
            GraphQL = MachineDocumentOps.Persisted(operationsNamespace: true),
            Services = services,
        };

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            Options(CountingDataContexts.Install)
        );
        _reads = _host.Services.GetRequiredService<CountingDataContexts>();
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        await ShelfHosts.StopAsync(_host);
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task Every_machine_document_is_registered_at_startup_under_the_reserved_prefix()
    {
        var documents = (await MachineDocumentOps.MachineDocumentsAsync(Database))
            .Where(d => d.Operation(WizardMachine.MachineId) != MachineDocumentOps.MachineChanged)
            .ToList();

        documents
            .Select(d => d.Operation(WizardMachine.MachineId))
            .Should()
            .BeEquivalentTo(
                MachineDocumentOps.WizardOperations,
                "start, discard, get and every client event of an exposed machine is registered under "
                    + MachineDocuments.ReservedIdPrefix
                    + ", per "
                    + Adr
            );
        documents
            .Should()
            .OnlyContain(d => d.TenantKey == "" && d.IsActive, "under the null tenant, active");
        foreach (var document in documents)
            document
                .Id.Should()
                .MatchRegex(
                    $"^{Regex.Escape(MachineDocuments.ReservedIdPrefix)}wizard_{document.Operation(WizardMachine.MachineId)}_[0-9A-Za-z]+$",
                    "the id is the reserved prefix, the machine, the operation and the hash"
                );
        documents
            .Select(d => d.Hash)
            .Should()
            .OnlyHaveUniqueItems(
                "the hash covers the document's text, which differs per operation"
            );
        (await MachineDocumentOps.MachineDocumentsAsync(Database, MemoMachine.MachineId))
            .Should()
            .BeEmpty("a machine the host does not expose has no documents");

        using var started = await _host.PostPersistedAsync(
            await MachineDocumentOps.IdOfAsync(Database, WizardMachine.MachineId, "start"),
            new { input = new { title = "Atlas" }, requestId = WizardOps.NewRequestId() },
            E2EHost.AliceKey
        );
        MachineDocumentOps
            .Succeeded(started, WizardMachine.MachineId, "start")
            .Should()
            .BeTrue("the registered start runs by its id: " + MachineDocumentOps.Show(started));
    }

    [Test]
    public async Task Restarting_a_node_writes_no_history_and_broadcasts_nothing_for_unchanged_documents()
    {
        var before = await MachineDocumentOps.MachineDocumentsAsync(Database);
        before
            .Should()
            .NotBeEmpty("the running node registered the wizard's documents, per " + Adr);
        var historyBefore = await MachineDocumentOps.HistoryRowsAsync(Database);
        var broadcasts = new CountingBroadcaster();

        var restarted = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            Options(services =>
                services.AddSingleton<IPersistedOperationBroadcaster>(broadcasts)
            ) with
            {
                ApplicationName = "machine_documents_restart",
            }
        );
        try
        {
            (await MachineDocumentOps.MachineDocumentsAsync(Database))
                .Should()
                .BeEquivalentTo(before, "an unchanged document is left as it is");
            (await MachineDocumentOps.HistoryRowsAsync(Database))
                .Should()
                .Be(historyBefore, "a restart that changes no document writes no history row");
            broadcasts
                .Published.Should()
                .BeEmpty(
                    "a restart that changes no document broadcasts nothing, so a rolling restart does not "
                        + "clear every node's cache"
                );
        }
        finally
        {
            await ShelfHosts.StopAsync(restarted);
        }
    }

    [Test]
    public async Task A_lookup_whose_stored_body_does_not_match_its_id_is_refused()
    {
        var getId = await MachineDocumentOps.IdOfAsync(Database, WizardMachine.MachineId, "get");
        var original = await TamperAsync(getId);
        using var alerts = new DocumentAlerts();
        try
        {
            using var answer = await _host.PostPersistedAsync(
                getId,
                new { id = Guid.NewGuid() },
                E2EHost.AliceKey
            );

            answer
                .RootElement.TryGetProperty("errors", out var errors)
                .Should()
                .BeTrue(
                    "a stored body that does not hash to its id is refused, per "
                        + Adr
                        + ": "
                        + MachineDocumentOps.Show(answer)
                );
            errors.GetArrayLength().Should().BePositive();
            (
                answer.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("__typename", out _)
            )
                .Should()
                .BeFalse("the tampered body never runs");
            alerts.Count.Should().BePositive("the mismatch is metered so it can be alerted on");
        }
        finally
        {
            await RestoreAsync(getId, original);
        }
    }

    [Test]
    public async Task Startup_repairs_a_tampered_machine_document_and_alerts()
    {
        var getId = await MachineDocumentOps.IdOfAsync(Database, WizardMachine.MachineId, "get");
        var original = await TamperAsync(getId);
        using var alerts = new DocumentAlerts();
        IHost? restarted = null;
        try
        {
            restarted = await E2EHost.StartAsync(
                Database,
                new CountingCharge(),
                Options() with
                {
                    ApplicationName = "machine_documents_repair",
                }
            );

            (await StoredBodyAsync(getId))
                .Should()
                .Be(
                    original,
                    "startup writes the generated body back over a tampered one, per " + Adr
                );
            alerts.Count.Should().BePositive("and the repair is metered so it can be alerted on");
        }
        finally
        {
            await ShelfHosts.StopAsync(restarted);
            await RestoreAsync(getId, original);
        }
    }

    [Test]
    public async Task Machine_documents_are_registered_under_the_null_tenant_and_a_tenant_row_does_not_shadow_them()
    {
        var startId = await MachineDocumentOps.IdOfAsync(
            Database,
            WizardMachine.MachineId,
            "start"
        );
        (await MachineDocumentOps.MachineDocumentsAsync(Database))
            .Should()
            .OnlyContain(
                d => d.TenantKey == "",
                "machine documents live under the null tenant, the one the hot path reads, per "
                    + Adr
            );

        await E2ESql.ExecuteAsync(
            Database,
            "INSERT INTO trax.persisted_operation "
                + "(tenant_key, id, operation_name, version, document, shape_fingerprint, is_active) "
                + "SELECT 'tenant-a', id, operation_name, version, @body, shape_fingerprint, true "
                + "FROM trax.persisted_operation WHERE tenant_key = '' AND id = @id",
            ("body", TamperedBody),
            ("id", startId)
        );
        try
        {
            using var answer = await _host.PostPersistedAsync(
                startId,
                new { input = new { title = "Atlas" }, requestId = WizardOps.NewRequestId() },
                E2EHost.AliceKey
            );

            MachineDocumentOps
                .Succeeded(answer, WizardMachine.MachineId, "start")
                .Should()
                .BeTrue(
                    "a tenant's row under the same id never replaces the machine's document: "
                        + MachineDocumentOps.Show(answer)
                );
        }
        finally
        {
            await E2ESql.ExecuteAsync(
                Database,
                "DELETE FROM trax.persisted_operation WHERE tenant_key = 'tenant-a'"
            );
        }
    }

    [Test]
    public async Task A_request_arriving_before_registration_completes_does_not_pin_a_miss()
    {
        var startId = await MachineDocumentOps.IdOfAsync(
            Database,
            WizardMachine.MachineId,
            "start"
        );
        // As on a fresh database: the documents are not there until the starting node registers them.
        await E2ESql.ExecuteAsync(
            Database,
            "DELETE FROM trax.persisted_operation WHERE starts_with(id, @prefix)",
            ("prefix", MachineDocuments.ReservedIdPrefix)
        );
        var probe = new EarlyRequest(startId);

        var node = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            Options(services =>
                services.AddHostedService(sp =>
                {
                    probe.Services = sp;
                    return probe;
                })
            ) with
            {
                ApplicationName = "machine_documents_early",
            }
        );
        try
        {
            probe.Ran.Should().BeTrue("the probe asked for the document while the node started");

            using var answer = await node.PostPersistedAsync(
                startId,
                new { input = new { title = "Atlas" }, requestId = WizardOps.NewRequestId() },
                E2EHost.AliceKey
            );

            MachineDocumentOps
                .Succeeded(answer, WizardMachine.MachineId, "start")
                .Should()
                .BeTrue(
                    "a lookup made before registration finished never leaves a cached miss behind, per "
                        + Adr
                        + ": "
                        + MachineDocumentOps.Show(answer)
                );
        }
        finally
        {
            await ShelfHosts.StopAsync(node);
        }
    }

    [Test]
    public async Task Unknown_reserved_prefix_ids_read_the_store_at_most_once_per_refresh_interval()
    {
        // A background read that another test's made-up ids asked for may still be due: count from once the reads
        // have stayed put for longer than a refresh interval.
        var settle = System.Diagnostics.Stopwatch.StartNew();
        int settled;
        do
        {
            settle
                .Elapsed.Should()
                .BeLessThan(E2EWait.DefaultTimeout, "the background reads settle");
            settled = _reads.Count;
            // measuring-interval: a read still due runs within one refresh interval of the miss that asked for it.
            await Task.Delay(MachineDocumentRegistry.MinRefreshInterval * 1.5);
        } while (_reads.Count != settled);

        // An ordinary unknown id, remembered as a miss: it must still be remembered afterwards.
        const string Plain = "plain_unknown_v1";
        using (await _host.PostPersistedAsync(Plain)) { }
        var before = _reads.Count;
        using (await _host.PostPersistedAsync(Plain)) { }
        (_reads.Count - before).Should().Be(0, "a remembered miss is not read again");

        for (var i = 0; i < 1_000; i++)
        {
            using var answer = await _host.PostPersistedAsync(
                $"{MachineDocuments.ReservedIdPrefix}wizard_submit_{Guid.NewGuid():N}"
            );
        }

        (_reads.Count - before)
            .Should()
            .Be(
                0,
                "an id under the reserved prefix that is not shaped like a generated one is refused without a "
                    + "database read, per "
                    + Adr
            );

        // Well-formed made-up ids, of the machine this node serves and of one it does not: each is answered at once,
        // and together they cause at most one read of the store per refresh interval.
        var flood = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            var machine = i % 2 == 0 ? WizardMachine.MachineId : "ledger";
            using var answer = await _host.PostPersistedAsync(MadeUp(machine, i));
        }
        // allowed-delay: lets the last background reads the flood asked for finish before counting.
        await Task.Delay(MachineDocumentRegistry.MinRefreshInterval * 3);
        flood.Stop();

        // One read per interval over the flood and the wait after it, and the first one at once.
        (_reads.Count - before)
            .Should()
            .BeLessThanOrEqualTo(
                (int)(flood.Elapsed / MachineDocumentRegistry.MinRefreshInterval) + 1,
                "made-up well-formed ids read the store at most once per refresh interval, however many arrive, "
                    + "per "
                    + Adr
            );
        var reads = _reads.Count;
        using (await _host.PostPersistedAsync(Plain)) { }
        (_reads.Count - reads)
            .Should()
            .Be(0, "the reserved ids do not fill the miss cache and evict what it remembered");
    }

    [Test]
    public async Task A_well_formed_made_up_id_is_answered_without_a_server_error_status()
    {
        foreach (var machine in new[] { WizardMachine.MachineId, "ledger" })
        {
            var (status, body) = await _host.PostPersistedWithStatusAsync(MadeUp(machine, 7));
            using (body)
            {
                MachineDocumentOps
                    .Codes(body)
                    .Should()
                    .ContainSingle()
                    .Which.Should()
                    .BeOneOf(DraftRefusalCodes.ServerOutdated, DraftRefusalCodes.ClientOutdated);
                status
                    .Should()
                    .BeInRange(
                        400,
                        499,
                        $"anyone can send a well-formed {machine} id, and a 5xx would count against a healthy node "
                            + "wherever a load balancer ejects nodes on server errors, per "
                            + Adr
                    );
            }
        }
    }

    // A well-formed id no version generated: the reserved prefix, a machine, an operation and a hash-shaped tail.
    private static string MadeUp(string machine, int i) =>
        $"{MachineDocuments.ReservedIdPrefix}{machine}_get_"
        + $"{i:x8}".PadLeft(MachineDocuments.IrHashLength + MachineDocuments.TextHashLength, '9');

    [Test]
    public async Task The_manifest_query_lists_every_document_with_its_hash_behind_the_operations_gate()
    {
        const string Manifest =
            "{ operations { machineDocuments { id machine operation irHash document } } }";

        using var asOperator = await _host.PostAsync(Manifest, apiKey: E2EHost.OperatorKey);
        WizardOps
            .FirstErrorMessage(asOperator)
            .Should()
            .BeNull(MachineDocumentOps.Show(asOperator));
        var listed = asOperator
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineDocuments")
            .EnumerateArray()
            .ToList();
        var stored = (await MachineDocumentOps.MachineDocumentsAsync(Database))
            .Where(d => d.IsActive)
            .ToList();
        stored.Should().NotBeEmpty();
        listed
            .Select(d => d.GetProperty("id").GetString())
            .Should()
            .BeEquivalentTo(
                stored.Select(d => d.Id),
                "the manifest lists every document, per " + Adr
            );
        listed
            .Should()
            .OnlyContain(d =>
                !string.IsNullOrEmpty(d.GetProperty("irHash").GetString())
                && !string.IsNullOrEmpty(d.GetProperty("document").GetString())
            );

        foreach (var caller in new[] { null, E2EHost.BobKey })
        {
            using var refused = await _host.PostAsync(Manifest, apiKey: caller);
            var data = refused.RootElement.TryGetProperty("data", out var d) ? d : default;
            (
                data.ValueKind != JsonValueKind.Object
                || data.GetProperty("operations").ValueKind == JsonValueKind.Null
            )
                .Should()
                .BeTrue(
                    $"the manifest is behind the operations gate, so {caller ?? "an anonymous caller"} "
                        + "reads nothing"
                );
        }
    }

    [TestCase(ShelfShape.RecursiveInput)]
    [TestCase(ShelfShape.RecursiveContext)]
    public async Task A_recursive_input_or_context_type_refuses_startup_rather_than_generating_forever(
        ShelfShape shape
    )
    {
        IHost? started = null;
        Exception? refusal = null;
        try
        {
            started = await ShelfHosts
                .StartAsync(Database, shape, $"machine_documents_{shape}".ToLowerInvariant())
                .WaitAsync(TimeSpan.FromMinutes(1));
            await started.ExecutorAsync().WaitAsync(TimeSpan.FromMinutes(1));
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            refusal = ex;
        }
        finally
        {
            await ShelfHosts.StopAsync(started);
        }

        refusal
            .Should()
            .NotBeNull(
                "a type that holds itself has no finite document or schema, so exposing it refuses "
                    + "startup, per "
                    + Adr
            );
        refusal!.ToString().Should().Contain(nameof(ShelfNode), "the refusal names the type");
    }

    // Writes a body nobody generated over a stored document, returning the original.
    private static async Task<string> TamperAsync(string id)
    {
        var original = await StoredBodyAsync(id);
        await E2ESql.ExecuteAsync(
            Database,
            "UPDATE trax.persisted_operation SET document = @body WHERE tenant_key = '' AND id = @id",
            ("body", TamperedBody),
            ("id", id)
        );
        return original;
    }

    private static Task RestoreAsync(string id, string body) =>
        E2ESql.ExecuteAsync(
            Database,
            "UPDATE trax.persisted_operation SET document = @body WHERE tenant_key = '' AND id = @id",
            ("body", body),
            ("id", id)
        );

    private static async Task<string> StoredBodyAsync(string id) =>
        (
            await E2ESql.ScalarAsync<string>(
                Database,
                "SELECT document FROM trax.persisted_operation WHERE tenant_key = '' AND id = @id",
                ("id", id)
            )
        )!;

    /// <summary>Records every persisted-operation change a node broadcasts.</summary>
    private sealed class CountingBroadcaster : IPersistedOperationBroadcaster
    {
        private readonly List<PersistedOperationChangedMessage> _published = [];

        public IReadOnlyList<PersistedOperationChangedMessage> Published
        {
            get
            {
                lock (_published)
                    return _published.ToList();
            }
        }

        public Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct)
        {
            lock (_published)
                _published.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A hosted service registered after Trax's: while the node starts, it asks the executor for a document by id, as
    /// a request that reaches a node before its registration finished does.
    /// </summary>
    private sealed class EarlyRequest(string documentId) : IHostedService
    {
        public bool Ran { get; private set; }

        public IServiceProvider? Services { get; set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Ran = true;
            if (Services is null)
                return;
            var executor = await Services
                .GetRequiredService<IRequestExecutorProvider>()
                .GetExecutorAsync("trax", cancellationToken);
            await executor.ExecuteAsync(
                OperationRequestBuilder.New().SetDocumentId(documentId).Build(),
                cancellationToken
            );
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Counts the measurements of any Trax instrument about machine documents (a mismatch, a repair), whatever its
    /// exact name.
    /// </summary>
    private sealed class DocumentAlerts : IDisposable
    {
        private readonly MeterListener _listener = new();
        private int _count;

        public DocumentAlerts()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (
                    instrument.Meter.Name.StartsWith("Trax", StringComparison.Ordinal)
                    && instrument.Name.Contains("document", StringComparison.OrdinalIgnoreCase)
                )
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>(
                (_, _, _, _) => Interlocked.Increment(ref _count)
            );
            _listener.SetMeasurementEventCallback<int>(
                (_, _, _, _) => Interlocked.Increment(ref _count)
            );
            _listener.Start();
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
