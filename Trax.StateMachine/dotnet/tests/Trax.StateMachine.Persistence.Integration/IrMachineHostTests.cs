using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A machine served from its IR file and its host entry, with no C# class, is served by the host as any other machine
/// is (ADR <c>Trax.Docs/adr/0058</c>): the host's startup checks find every rule they ask for, the draft service starts
/// and advances it under the host entry's rules, and a state that invokes a train launches it and takes its outcome.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class IrMachineHostTests(StoreProvider provider)
{
    private const string Rider = "Rider";

    private InstanceHost _host = null!;
    private RecordingLauncher _launcher = null!;
    private IMachine _turnstile = null!;
    private IMachine _ingest = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        // Renamed, so they do not collide with the hand-written machines the host's scan registers.
        var turnstile = Ir("turnstile", "ir-turnstile");
        var ingest = Ir("ingest", "ir-ingest");
        var riders = new AccessRule([
            [Rider],
        ]);
        var anyone = new TriggerAccess(AccessRule.AnyAuthenticated);
        var machines = IrMachines.Create(
            [
                new IrMachines.Source(
                    turnstile,
                    Host(turnstile) with
                    {
                        Start = riders,
                        Read = riders,
                        Triggers = new Dictionary<string, TriggerAccess>
                        {
                            ["Coin"] = new(riders),
                            ["Push"] = anyone,
                        },
                    }
                ),
                new IrMachines.Source(
                    ingest,
                    Host(ingest) with
                    {
                        Start = AccessRule.AnyAuthenticated,
                        Read = AccessRule.AnyAuthenticated,
                        Triggers = new Dictionary<string, TriggerAccess>
                        {
                            ["Start"] = anyone,
                            ["Retry"] = anyone,
                            ["Abandon"] = anyone,
                            ["Approve"] = anyone,
                        },
                        Invokes = new Dictionary<string, HostInvoke>
                        {
                            ["Fetching"] = new(
                                "Fetching",
                                typeof(IFetchFromIrTrain).FullName!,
                                new Dictionary<string, string> { ["source"] = "source" }
                            ),
                        },
                    }
                ),
            ],
            [typeof(IFetchFromIrTrain).Assembly]
        );
        (_turnstile, _ingest) = (machines[0], machines[1]);
        _launcher = new RecordingLauncher();
        _host = InstanceHost.Create(
            provider,
            launcher: _launcher,
            services: s => s.AddIrMachines(machines)
        );
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    private static string NewUser() => $"rider-{Guid.NewGuid():N}";

    [Test]
    public async Task The_hosts_startup_checks_find_every_rule_they_ask_for()
    {
        var problems = await MachineRulesStartupValidator.Problems(
            [_turnstile, _ingest],
            new StateMachineOptions(),
            caller: null
        );

        problems
            .Should()
            .BeEmpty("the host entry gives every client event, start, read and discard its rule");
    }

    [Test]
    public async Task A_machine_with_no_read_rule_is_refused_at_startup()
    {
        var ir = Ir("turnstile", "ir-turnstile-unread");
        var unread = IrMachines.Create(
            new IrMachines.Source(
                ir,
                Host(ir) with
                {
                    Start = AccessRule.AnyAuthenticated,
                    Triggers = new Dictionary<string, TriggerAccess>
                    {
                        ["Coin"] = new(AccessRule.AnyAuthenticated),
                        ["Push"] = new(AccessRule.AnyAuthenticated),
                    },
                }
            ),
            []
        );

        var problems = await MachineRulesStartupValidator.Problems(
            [unread],
            new StateMachineOptions(),
            null
        );

        problems.Should().ContainSingle().Which.Should().Contain("get, changed, discard");
    }

    [Test]
    public async Task A_client_is_held_to_the_host_entrys_rules()
    {
        var user = NewUser();
        using var scope = _host.Scope();
        var service = _host.Service(scope, "ir-turnstile");

        (await service.Start(DeskRoles.Client(user), new JsonObject(), requestId: "s0"))
            .Should()
            .BeOfType<StartOutcome.Rejected>()
            .Which.Code.Should()
            .Be(RefusalCode.Forbidden, "only a rider may start one");

        var started = (
            await service.Start(DeskRoles.Client(user, Rider), new JsonObject(), requestId: "s1")
        )
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject;
        var id = started.Id;

        (
            await service.Advance(
                DeskRoles.Client(user),
                id,
                "Coin",
                new JsonObject { ["coin"] = "quarter" },
                requestId: "a1"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be(RefusalCode.Forbidden, "Coin requires a rider");

        (
            await service.Advance(
                DeskRoles.Client(user, Rider),
                id,
                "Coin",
                new JsonObject { ["coin"] = "quarter" },
                requestId: "a2"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");

        (await service.Advance(DeskRoles.Client(user), id, "Push", requestId: "a3"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>("Push admits any signed-in caller")
            .Which.Snapshot.State.Should()
            .Be("Locked");
    }

    [Test]
    public async Task An_invoking_state_runs_the_named_train_and_takes_its_outcome()
    {
        var user = NewUser();
        Guid id;
        using (var scope = _host.Scope())
        {
            var service = _host.Service(scope, "ir-ingest");
            id = (await service.Start(DeskRoles.Client(user), new JsonObject(), requestId: "s1"))
                .Should()
                .BeOfType<StartOutcome.Started>()
                .Subject.Id;
            (await service.Advance(DeskRoles.Client(user), id, "Start", requestId: "a1"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Fetching");
        }

        var launch = _launcher
            .Launches.Should()
            .ContainSingle(l => l.InvokedBy.InstanceId == id)
            .Subject;
        launch.TrainType.Should().Be(typeof(IFetchFromIrTrain));
        launch
            .Input.Should()
            .Be(new FetchFromIrInput("s3://bucket/partition-1"), "the host entry maps source");

        var token = (await _host.Rows(id)).Single().InvokeToken!;
        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.Where(w => w.ExternalId == token)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled));
        using (var scope = _host.Scope())
            (await scope.ServiceProvider.GetRequiredService<IInvokedRunOutcomes>().Deliver(token))
                .Should()
                .Be("Cancelled", "the cancelled outcome's edge goes where the IR says");
    }

    private static JsonObject Ir(string name, string id)
    {
        var ir = JsonNode.Parse(File.ReadAllText(IrFile(name)))!.AsObject();
        ir["id"] = id;
        return ir;
    }

    private static string IrFile(string name)
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var file = Path.Combine(
                dir.FullName,
                "conformance",
                "machines",
                name,
                $"{name}.ir.json"
            );
            if (File.Exists(file))
                return file;
        }
        throw new FileNotFoundException($"conformance/machines/{name}/{name}.ir.json");
    }

    private static HostMachine Host(JsonObject ir) =>
        new(
            ir["id"]!.GetValue<string>(),
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
            ),
            null,
            null,
            null,
            new Dictionary<string, TriggerAccess>(),
            new Dictionary<string, HostInvoke>()
        );
}

/// <summary>The input of the train the ingest machine served from its IR invokes.</summary>
public sealed record FetchFromIrInput(string Source);

/// <summary>Its output, the schema the IR's done outcome declares.</summary>
public sealed record FetchFromIrOutput
{
    public string Fingerprint { get; init; } = "";
    public bool Unsure { get; init; }
}

/// <summary>The train, named by its interface; the recording launcher never runs it.</summary>
public interface IFetchFromIrTrain : IServiceTrain<FetchFromIrInput, FetchFromIrOutput>;
