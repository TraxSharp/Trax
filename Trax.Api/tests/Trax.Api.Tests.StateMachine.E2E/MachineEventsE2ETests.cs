using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Each typed event runs through the internal advance or send train, so audit, metadata and error mapping stay on
/// one path, and returns the snapshot the server recorded with its revision. Those internal runs stay off the
/// lifecycle feed; input is validated against the event's record, size cap included, before anything is written; a
/// refused guard is data; a sensitive input field is masked in the run's recorded input; an operation holds at most
/// one machine event; and two reads in one request share the request's data context safely.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineEventsE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_events";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine), typeof(MemoMachine)],
                // With the operations surface exposed, every train's lifecycle is published to the operator, which
                // is where an internal machine train would leak.
                ExposeOperations = true,
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task Each_event_runs_through_the_advance_train_and_returns_the_servers_snapshot_with_revision()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey, title: "Atlas");
        var advancesBefore = await RunsOf<IAdvanceSnapshot>();
        var sendsBefore = await RunsOf<ISendSnapshot>();

        var described = await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput(title: "Atlas", pages: 4)
        );
        var paid = await _host.AdvanceWizardAsync(E2EHost.AliceKey, "pay", started.Id);

        described.StateIs(WizardState.Paying).Should().BeTrue(described.Raw.GetRawText());
        described.Context.GetProperty("pages").GetInt32().Should().Be(4);
        described
            .Revision.Should()
            .BeGreaterThan(
                started.Revision!.Value,
                "every answer carries the revision the server recorded, per " + Adr
            );
        paid.StateIs(WizardState.Approving).Should().BeTrue(paid.Raw.GetRawText());
        paid.Context.GetProperty("receipt").GetString().Should().StartWith("receipt-");
        paid.Revision.Should().BeGreaterThan(described.Revision!.Value);

        (await RunsOf<IAdvanceSnapshot>())
            .Should()
            .Be(
                advancesBefore + 1,
                "describe ran through the advance train, so its run is recorded"
            );
        (await RunsOf<ISendSnapshot>())
            .Should()
            .Be(sendsBefore + 1, "the effect-bound pay ran through the send train");
    }

    [Test]
    public async Task Internal_machine_trains_are_not_broadcast_on_the_lifecycle_feed()
    {
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.OperatorKey);
        await ws.SubscribeAsync("feed", "subscription { onTrainCompleted { trainName output } }");

        // graphql-transport-ws sends no ack for subscribe, so the subscription is live only once a warm-up run
        // reaches it. One published before the server registered it is never delivered, so warm up until one is.
        using (var deadline = new CancellationTokenSource(GraphQLWs.DefaultTimeout))
        {
            JsonElement? warm;
            do
            {
                deadline.IsCancellationRequested.Should().BeFalse("a warm-up run reaches the feed");
                await RunSentinelAsync("warm-up");
                warm = await ws.TryNextAsync("feed", TimeSpan.FromSeconds(1));
            } while (warm is null);
            CompletedPing(warm.Value).Should().BeTrue(warm.Value.GetRawText());
        }

        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput()
        );
        var sentinel = "sentinel-" + Guid.NewGuid().ToString("N");
        await RunSentinelAsync(sentinel);

        // Warm-ups that were still on their way may come first; anything else before the sentinel leaked.
        while (true)
        {
            var completed = (await ws.NextDataAsync("feed")).GetProperty("onTrainCompleted");
            if (completed.GetProperty("output").GetRawText().Contains(sentinel))
                break;
            CompletedPing(completed, "warm-up")
                .Should()
                .BeTrue(
                    "the machine's internal start and advance runs are left out of the lifecycle broadcast, so "
                        + "only warm-up pings come before the sentinel, per "
                        + Adr
                        + ": "
                        + completed.GetRawText()
                );
        }
    }

    // Whether a lifecycle event (or the message carrying it) is a completed ping, echoing `value` when given.
    private static bool CompletedPing(JsonElement message, string? value = null)
    {
        var completed = message.TryGetProperty("payload", out var payload)
            ? payload.GetProperty("data").GetProperty("onTrainCompleted")
            : message;
        return completed.GetProperty("trainName").GetString()?.Contains(nameof(IPing)) == true
            && (value is null || completed.GetProperty("output").GetRawText().Contains(value));
    }

    private static IEnumerable<TestCaseData> InvalidInputs()
    {
        yield return new TestCaseData("{ title: \"Atlas\", pages: \"three\" }").SetArgDisplayNames(
            "wrong kind"
        );
        yield return new TestCaseData("{ pages: 2 }").SetArgDisplayNames("missing required field");
        yield return new TestCaseData(
            "{ title: \"Atlas\", pages: 2, colour: \"red\" }"
        ).SetArgDisplayNames("unknown field");
        yield return new TestCaseData(
            "{ title: \"Atlas\", pages: 2, note_text: \"longer than ten characters\" }"
        ).SetArgDisplayNames("JsonPropertyName field past its MaxLength");
        yield return new TestCaseData(
            $"{{ title: {WizardOps.Literal(new string('x', 1024 * 1024))}, pages: 2 }}"
        ).SetArgDisplayNames("input over the size cap");
    }

    [TestCaseSource(nameof(InvalidInputs))]
    public async Task Invalid_input_is_refused_as_invalid_input_and_nothing_is_written(string input)
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, started.Id);
        var runsBefore = await E2ESql.CountMachineRunsAsync(Database);

        using var doc = await _host.PostAsync(
            WizardOps.Event("describe", started.Id, WizardOps.NewRequestId(), input),
            apiKey: E2EHost.AliceKey
        );

        var refusedByValidation = WizardOps.ErrorCodes(doc).Count > 0;
        if (!refusedByValidation)
            WizardOps
                .Result(doc, "describe")
                .ProblemCode.Should()
                .BeOneOf(
                    [RefusalCode.InvalidInput.ToWire(), RefusalCode.TooLarge.ToWire()],
                    "input is validated against the event's record before the edge runs, per " + Adr
                );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, started.Id))
            .Should()
            .Be(before, "refused input writes nothing");
        if (refusedByValidation)
            (await E2ESql.CountMachineRunsAsync(Database))
                .Should()
                .Be(runsBefore, "a document refused by validation runs nothing");
    }

    [Test]
    public async Task A_refused_guard_is_data_not_an_error()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);

        using var doc = await _host.PostAsync(
            WizardOps.Describe(started.Id, WizardOps.NewRequestId(), pages: 0),
            apiKey: E2EHost.AliceKey
        );

        var refused = WizardOps.Result(doc, "describe");
        refused.ProblemCode.Should().Be(RefusalCode.GuardFailed.ToWire());
        refused
            .ProblemMessage.Should()
            .Be(
                WizardMachine.NeedsPages,
                "a refused guard is answered with its Because text as data, never an error, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_events_sensitive_input_field_is_masked_in_the_runs_recorded_input()
    {
        var secret = $"secret-{Guid.NewGuid():N}";
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        var runsBefore = await E2ESql.CountMachineRunsAsync(Database);

        await _host.AdvanceWizardAsync(
            E2EHost.AliceKey,
            "describe",
            started.Id,
            WizardOps.DetailsInput(secret: secret)
        );

        var inputs = await E2ESql.MachineRunInputsAsync(Database);
        inputs.Count.Should().BeGreaterThan((int)runsBefore, "the advance run is recorded");
        inputs
            .Should()
            .NotContain(
                i => i.Contains(secret, StringComparison.Ordinal),
                "a [TraxSensitive] input field is masked in every recorded copy of the run's input, per "
                    + "central ADR 0027 and "
                    + Adr
            );
    }

    private static IEnumerable<TestCaseData> TwoEventOperations()
    {
        yield return new TestCaseData(true).SetArgDisplayNames("two events on one instance");
        yield return new TestCaseData(false).SetArgDisplayNames("events on two machines");
    }

    [TestCaseSource(nameof(TwoEventOperations))]
    public async Task An_operation_with_two_machine_event_fields_is_refused(bool sameInstance)
    {
        var wizard = await _host.StartWizardAsync(E2EHost.AliceKey);
        var memo = await StartMemoAsync();
        var wizardBefore = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, wizard.Id);
        var memoBefore = await E2ESql.DraftAsync(Database, MemoMachine.MachineId, memo);

        var describe = WizardOps.EventField(
            "describe",
            wizard.Id,
            WizardOps.NewRequestId(),
            WizardOps.DetailsInput(),
            alias: "a"
        );
        var document = sameInstance
            ? $"mutation {{ machines {{ wizard {{ {describe} {WizardOps.EventField("describe", wizard.Id, WizardOps.NewRequestId(), WizardOps.DetailsInput(title: "Other"), alias: "b")} }} }} }}"
            : $"mutation {{ machines {{ wizard {{ {describe} }} memo {{ file(id: {WizardOps.Literal(memo.ToString())}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ id problem {{ code }} }} }} }} }}";

        using var doc = await _host.PostAsync(document, apiKey: E2EHost.AliceKey);

        WizardOps
            .ErrorCodes(doc)
            .Should()
            .NotBeEmpty(
                "fields under a namespace run in parallel, so an operation holds at most one machine event, per "
                    + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, wizard.Id))
            .Should()
            .Be(wizardBefore, "the operation is refused before either event runs");
        (await E2ESql.DraftAsync(Database, MemoMachine.MachineId, memo)).Should().Be(memoBefore);
    }

    [Test]
    public async Task Two_aliased_gets_in_one_request_share_the_scoped_DbContext_safely()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        var id = WizardOps.Literal(started.Id.ToString());
        var document =
            $"{{ machines {{ wizard {{ a: get(id: {id}) {{ {WizardOps.ReadFields} }} b: get(id: {id}) {{ {WizardOps.ReadFields} }} }} }} }}";

        for (var i = 0; i < 50; i++)
        {
            using var doc = await _host.PostAsync(document, apiKey: E2EHost.AliceKey);

            WizardOps
                .ErrorCodes(doc)
                .Should()
                .BeEmpty(
                    $"two gets in one request must not use the scoped data context at once (run {i}), per "
                        + Adr
                        + ": "
                        + doc.RootElement.GetRawText()
                );
            var wizard = doc
                .RootElement.GetProperty("data")
                .GetProperty("machines")
                .GetProperty("wizard");
            wizard.GetProperty("a").ValueKind.Should().Be(JsonValueKind.Object);
            wizard.GetProperty("b").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    private async Task<long> RunsOf<TTrain>() =>
        await E2ESql.ScalarAsync<long>(
            Database,
            "SELECT count(*) FROM trax.metadata WHERE name = @name",
            ("name", typeof(TTrain).FullName!)
        );

    private async Task RunSentinelAsync(string value)
    {
        using var doc = await _host.PostAsync(
            $"{{ discover {{ ping(input: {{ value: {WizardOps.Literal(value)} }}) {{ value }} }} }}",
            apiKey: E2EHost.AliceKey
        );
        WizardOps.ErrorCodes(doc).Should().BeEmpty(doc.RootElement.GetRawText());
    }

    private async Task<Guid> StartMemoAsync()
    {
        using var doc = await _host.PostAsync(
            $"mutation {{ machines {{ memo {{ start(input: {{ text: \"note\" }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ id problem {{ code }} }} }} }} }}",
            apiKey: E2EHost.AliceKey
        );
        WizardOps.FailOnErrors(doc, "memo.start");
        return Guid.Parse(
            doc.RootElement.GetProperty("data")
                .GetProperty("machines")
                .GetProperty("memo")
                .GetProperty("start")
                .GetProperty("id")
                .GetString()!
        );
    }
}
