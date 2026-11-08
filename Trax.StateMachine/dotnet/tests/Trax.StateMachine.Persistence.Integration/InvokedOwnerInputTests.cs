using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A host file maps a train's input field to <c>$owner</c>, the instance owner's qualified principal id: the train
/// takes its subject as input and the machine guarantees it. The value is in no context, so no client writes it.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokedOwnerInputTests(StoreProvider provider)
{
    private InstanceHost _host = null!;
    private RecordingLauncher _launcher = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var ir = JsonNode.Parse(File.ReadAllText(IrFile("ingest")))!.AsObject();
        ir["id"] = "ir-loan";
        var anyone = new TriggerAccess(AccessRule.AnyAuthenticated);
        var machines = IrMachines.Create(
            [
                new IrMachines.Source(
                    ir,
                    new HostMachine(
                        "ir-loan",
                        Convert.ToHexStringLower(
                            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(ir)))
                        ),
                        null,
                        AccessRule.AnyAuthenticated,
                        AccessRule.AnyAuthenticated,
                        new Dictionary<string, TriggerAccess>
                        {
                            ["Start"] = anyone,
                            ["Retry"] = anyone,
                            ["Abandon"] = anyone,
                            ["Approve"] = anyone,
                        },
                        new Dictionary<string, HostInvoke>
                        {
                            ["Fetching"] = new(
                                "Fetching",
                                typeof(IBorrowFromIrTrain).FullName!,
                                new Dictionary<string, string>
                                {
                                    ["source"] = "source",
                                    ["memberId"] = HostInvoke.Owner,
                                }
                            ),
                        }
                    )
                ),
            ],
            [typeof(IBorrowFromIrTrain).Assembly]
        );
        _launcher = new RecordingLauncher();
        _host = InstanceHost.Create(
            provider,
            launcher: _launcher,
            services: s => s.AddIrMachines(machines)
        );
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task The_train_receives_the_owners_id()
    {
        var ada = $"jwt:ada-{Guid.NewGuid():N}";
        using var scope = _host.Scope();
        var service = _host.Service(scope, "ir-loan");
        var id = (await service.Start(DeskRoles.Client(ada), new JsonObject(), requestId: "s1"))
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject.Id;

        (await service.Advance(DeskRoles.Client(ada), id, "Start", requestId: "a1"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Fetching");

        _launcher
            .Launches.Should()
            .ContainSingle(l => l.InvokedBy.InstanceId == id)
            .Which.Input.Should()
            .Be(new BorrowFromIrInput("s3://bucket/partition-1", ada));
    }

    [Test]
    public async Task A_client_save_that_writes_the_owner_field_is_refused()
    {
        var ada = $"jwt:ada-{Guid.NewGuid():N}";
        using var scope = _host.Scope();
        var service = _host.Service(scope, "ir-loan");
        var id = (await service.Start(DeskRoles.Client(ada), new JsonObject(), requestId: "s1"))
            .Should()
            .BeOfType<StartOutcome.Started>()
            .Subject.Id;
        JsonObject Snapshot(JsonObject context) =>
            new()
            {
                ["machine"] = "ir-loan",
                ["version"] = 1,
                ["state"] = "Idle",
                ["context"] = context,
            };

        foreach (var field in new[] { "memberId", HostInvoke.Owner })
        {
            var forged = Snapshot(new() { ["source"] = "s3://other", [field] = "jwt:mallory" });

            (await service.Autosave(ada, id, forged.ToJsonString()))
                .Should()
                .BeOfType<AutosaveResult.Rejected>(
                    $"'{field}' is no context field, so a client cannot write the owner"
                );
        }

        (
            await service.Advance(
                DeskRoles.Client(ada),
                id,
                "Start",
                new JsonObject { ["memberId"] = "jwt:mallory" },
                requestId: "a1"
            )
        )
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>("no trigger takes the owner as input either");
        _launcher.Launches.Should().NotContain(l => l.InvokedBy.InstanceId == id);
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
}

/// <summary>A train whose subject is its input: the member the machine's owner is.</summary>
public sealed record BorrowFromIrInput(string Source, string MemberId);

/// <summary>The train, named by its interface; the recording launcher never runs it.</summary>
public interface IBorrowFromIrTrain : IServiceTrain<BorrowFromIrInput, FetchFromIrOutput>;
