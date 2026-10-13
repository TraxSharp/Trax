using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A trigger's input is checked against its declared schema on every path that fires a trigger: the draft service,
/// the generic advance junction, and a host's trusted advance through <see cref="IMachineInstances"/>, which is
/// trusted for rules but never for input. No operator path fires a trigger with an input: the operator's only write
/// to an instance is a cancel, which applies the run's <c>Cancelled</c> outcome. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TriggerInputValidationPathTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "u1";

    private const string Machine = "declarative-turnstile";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider);

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static string LockedJson(string machine) =>
        new JsonObject
        {
            ["machine"] = machine,
            ["version"] = 1,
            ["state"] = "Locked",
            ["context"] = new JsonObject(),
        }.ToJsonString();

    // An accepted coin with a field CoinInput does not declare.
    private static JsonObject UnknownField => new() { ["coin"] = "quarter", ["x"] = 1 };

    // Past the 64 KiB cap many times over, and an accepted coin besides, so only the cap refuses it.
    private static JsonObject TenMegabytes =>
        new() { ["coin"] = "quarter", ["pad"] = new string('q', 10 * 1024 * 1024) };

    /// <summary>A path a trigger is fired through, and the id of the row it writes.</summary>
    private sealed record Path(
        Guid Id,
        SnapshotOwnerKind Kind,
        Func<JsonNode?, Task<string?>> Fire
    );

    // Creates the instance the path fires on, and returns how to fire Coin through it: null when the trigger was
    // applied, else the code it was refused with.
    private async Task<Path> Arrange(string path)
    {
        switch (path)
        {
            case "service":
            case "junction":
            {
                var id = Guid.NewGuid();
                using (var scope = _host.Scope())
                    (await _host.Service(scope, Machine).Autosave(User, id, LockedJson(Machine)))
                        .Should()
                        .BeOfType<AutosaveResult.Saved>();
                return new Path(
                    id,
                    SnapshotOwnerKind.User,
                    path == "service"
                        ? input => ViaService(id, input)
                        : input => ViaJunction(id, input)
                );
            }
            case "instances":
            {
                var key = MachineKey.Of("input", Guid.NewGuid().ToString());
                var instance = await _host.Start<SystemDeclarativeTurnstileMachine>(key);
                return new Path(
                    instance.Id,
                    SnapshotOwnerKind.System,
                    input => ViaInstances(key, input)
                );
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    private async Task<string?> ViaService(Guid id, JsonNode? input, string? requestId = null)
    {
        using var scope = _host.Scope();
        return Code(
            await _host.Service(scope, Machine).Advance(User, id, "Coin", input, requestId)
        );
    }

    private async Task<string?> ViaJunction(Guid id, JsonNode? input)
    {
        using var scope = _host.Scope();
        var registry = scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>();
        var output = await new AdvanceSnapshotJunction(registry, new FakePrincipal(User)).Run(
            new AdvanceSnapshotInput
            {
                Machine = Machine,
                Id = id,
                Trigger = "Coin",
                Input = input?.ToJsonString(),
            }
        );
        return output.Problem?.Code;
    }

    private async Task<string?> ViaInstances(MachineKey key, JsonNode? input)
    {
        using var scope = _host.Scope();
        return Code(
            await scope
                .ServiceProvider.GetRequiredService<IMachineInstances>()
                .Advance<SystemDeclarativeTurnstileMachine>(key, "Coin", input)
        );
    }

    private static string? Code(AdvanceOutcome outcome) =>
        outcome switch
        {
            AdvanceOutcome.Advanced => null,
            AdvanceOutcome.Rejected rejected => rejected.Reason,
            AdvanceOutcome.LoadError error => error.Code,
            AdvanceOutcome.NotFound => "not-found",
            AdvanceOutcome.Conflict => "conflict",
            _ => "unknown",
        };

    [TestCase("service")]
    [TestCase("junction")]
    [TestCase("instances")]
    public async Task Every_path_validates_trigger_input_and_refuses_unknown_fields(string path)
    {
        var arranged = await Arrange(path);
        var before = (await _host.Rows(arranged.Id)).Single();

        var refused = await arranged.Fire(UnknownField);

        refused
            .Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"a field the input type does not declare is refused on every path, trusted or not. See {Adr}"
            );
        var after = (await _host.Rows(arranged.Id)).Single();
        after.State.Should().Be(before.State);
        after.Context.Should().Be(before.Context);
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
        (await _host.Revision(arranged.Id, arranged.Kind))
            .Should()
            .Be(1, "the row was written once, when it was created");
    }

    [TestCase("service")]
    [TestCase("instances")]
    public async Task Input_size_is_capped_before_validation_on_every_path(string path)
    {
        var arranged = await Arrange(path);
        var before = (await _host.Rows(arranged.Id)).Single();

        var refused = await arranged.Fire(TenMegabytes);

        refused
            .Should()
            .Be(
                RejectionReasons.TooLarge,
                $"an input past the cap is refused before it is parsed or validated, on every path. See {Adr}"
            );
        var after = (await _host.Rows(arranged.Id)).Single();
        after.ConcurrencyToken.Should().Be(before.ConcurrencyToken, "nothing was written");
        after.Context.Should().Be(before.Context);
    }

    [Test]
    public async Task An_invalid_input_records_no_request_id_so_a_corrected_retry_fires()
    {
        var arranged = await Arrange("service");

        (await ViaService(arranged.Id, UnknownField, requestId: "r1"))
            .Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"the input carries a field CoinInput lacks. See {Adr}"
            );
        var refused = (await _host.Rows(arranged.Id)).Single();
        refused.LastRequestId.Should().BeNull("a refused request records nothing to replay");
        refused.State.Should().Be("Locked");

        (await ViaService(arranged.Id, new JsonObject { ["coin"] = "quarter" }, requestId: "r1"))
            .Should()
            .BeNull("the corrected retry under the same id fires as a new request");
        var fired = (await _host.Rows(arranged.Id)).Single();
        fired.State.Should().Be("Unlocked");
        JsonNode.Parse(fired.Context)!["paidWith"]!.GetValue<string>().Should().Be("quarter");
        fired.LastRequestId.Should().Be("r1");
    }

    [Test]
    public async Task The_effect_runner_receipt_still_commits_on_an_effect_bound_trigger_with_WithInput()
    {
        var id = Guid.NewGuid();
        // The generic save does not serve a machine with an effect, so the draft is written through the store.
        await _host.Seed(User, id, TestDb.Parse(ReceiptOrderMachine.ReviewJson));

        AdvanceOutcome sent;
        using (var scope = _host.Scope())
            sent = await scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .EffectRunner(ReceiptOrderMachine.Id)!
                .Run(User, id, "send-1");

        sent.Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                $"the runner's receipt input matches the declared input, so validation keeps RunsOnce working. See {Adr}"
            )
            .Which.Snapshot.State.Should()
            .Be("Placed");
        var row = (await _host.Rows(id)).Single();
        row.State.Should().Be("Placed");
        JsonNode.Parse(row.Context)!["receipt"]!.GetValue<string>().Should().StartWith("receipt-");
    }
}
