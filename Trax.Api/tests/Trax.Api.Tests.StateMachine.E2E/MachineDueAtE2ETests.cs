using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The due time of an instance's timer travels beside its snapshot on every answer: <c>start</c>, each event, an
/// effect's send, <c>get</c> and every <c>machineChanged</c> push carry the row's <c>due_at</c>, and null once the
/// instance is in a state with no timer. A timer whose duration the context holds is armed from the typed surface,
/// which writes and reads a <see cref="TimeSpan"/> as the ISO-8601 duration the engine reads.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineDueAtE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_due_at";

    private const string Fields =
        "id revision state dueAt context { patience } problem { code message }";

    private const string WizardFields =
        $"id revision state dueAt dueAtKnown {WizardOps.ContextFields} problem {{ code message }}";

    private IHost _host = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions { Served = [typeof(WizardMachine), typeof(SnoozeMachine)] }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    #region The wizard: a fixed timer

    [Test]
    public async Task Every_answer_carries_the_rows_due_time_and_null_outside_a_timed_state()
    {
        var started = await Wizard(
            WizardOps.Start("Atlas", WizardOps.NewRequestId()).Replace(Old, WizardFields),
            "start"
        );
        DueAt(started).Should().BeNull("Naming has no timer");

        var described = await Wizard(
            WizardOps
                .Describe(started.GetProperty("id").GetGuid(), WizardOps.NewRequestId())
                .Replace(Old, WizardFields),
            "describe"
        );
        DueAt(described).Should().BeNull("Paying has no timer");

        var id = started.GetProperty("id").GetGuid();
        var paid = await Wizard(
            WizardOps.Event("pay", id, WizardOps.NewRequestId()).Replace(Old, WizardFields),
            "pay"
        );
        var row = await RowDueAt(WizardMachine.MachineId, id);
        row.Should().NotBeNull("Approving has a timer, so the row holds its due time");
        SameInstant(
            DueAt(paid),
            row,
            "the send's answer carries the due time the row was given, per " + Adr
        );

        var read = await Get(WizardMachine.MachineId, "wizard", id, WizardFields);
        SameInstant(DueAt(read), row, "get answers the row's due time");

        var approved = await Wizard(
            WizardOps.Event("approve", id, WizardOps.NewRequestId()).Replace(Old, WizardFields),
            "approve"
        );
        DueAt(approved).Should().BeNull("Building has no timer");
        (await RowDueAt(WizardMachine.MachineId, id)).Should().BeNull();
    }

    [Test]
    public async Task A_push_carries_the_due_time_and_leaving_the_timed_state_pushes_null()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync(
            "1",
            $"subscription {{ machineChanged(machine: \"wizard\", id: {WizardOps.Literal(paying.Id.ToString())}) {{ revision state dueAt gone problem {{ code message }} }} }}"
        );
        var first = await ws.NextDataAsync("1");
        DueAt(first.GetProperty("machineChanged")).Should().BeNull("Paying has no timer");

        // Another connection moves the instance into the timed state.
        var paid = await _host.AdvanceWizardAsync(E2EHost.AliceKey, "pay", paying.Id);
        var pushed = await NextAtLeast(ws, paid.Revision!.Value);
        var row = await RowDueAt(WizardMachine.MachineId, paying.Id);
        row.Should().NotBeNull();
        SameInstant(DueAt(pushed), row, "the push carries the due time the row holds, per " + Adr);

        var approved = await _host.AdvanceWizardAsync(E2EHost.AliceKey, "approve", paying.Id);
        var left = await NextAtLeast(ws, approved.Revision!.Value);
        DueAt(left).Should().BeNull("leaving the timed state pushes no due time");
    }

    [Test]
    public async Task A_replay_the_instance_has_moved_past_says_its_due_time_is_unknown()
    {
        var paying = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var id = paying.Id;
        var pay = WizardOps.NewRequestId();
        var paid = await Wizard(WizardOps.Event("pay", id, pay).Replace(Old, WizardFields), "pay");
        DueAt(paid).Should().NotBeNull("Approving has a timer");
        Known(paid).Should().BeTrue("the send knows the due time it armed");

        // A retry while the row is still at the recorded revision knows the row's due time.
        var again = await Wizard(WizardOps.Event("pay", id, pay).Replace(Old, WizardFields), "pay");
        Known(again).Should().BeTrue("the row is still at the revision the send recorded");
        SameInstant(DueAt(again), DueAt(paid), "the replay at the recorded revision carries it");

        await Wizard(
            WizardOps.Event("approve", id, WizardOps.NewRequestId()).Replace(Old, WizardFields),
            "approve"
        );

        var replayed = await Wizard(
            WizardOps.Event("pay", id, pay).Replace(Old, WizardFields),
            "pay"
        );
        replayed
            .GetProperty("state")
            .GetString()
            .Should()
            .Be(nameof(WizardState.Approving), "the retry replays the recorded result");
        replayed
            .GetProperty("revision")
            .GetInt64()
            .Should()
            .Be(paid.GetProperty("revision").GetInt64());
        Known(replayed)
            .Should()
            .BeFalse(
                "the row has moved past the recorded revision, so the server cannot know the due time "
                    + "Approving had, and says so rather than answer null as though it were cleared, per "
                    + Adr
            );
        DueAt(replayed).Should().BeNull();
    }

    #endregion

    #region The snooze: a duration the context holds

    [Test]
    public async Task A_context_duration_is_armed_from_the_typed_surface_and_read_back()
    {
        var started = await Snooze(
            $"mutation {{ machines {{ snooze {{ start(input: {{ patience: \"PT5M\" }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ {Fields} }} }} }} }}",
            "start"
        );
        Problem(started).Should().BeNull("a five-minute patience is within the timer's bounds");
        started.GetProperty("state").GetString().Should().Be(nameof(SnoozeState.Waiting));
        started
            .GetProperty("context")
            .GetProperty("patience")
            .GetString()
            .Should()
            .Be("PT5M", "a duration crosses the typed surface as ISO-8601");

        var id = started.GetProperty("id").GetGuid();
        var row = await RowDueAt(SnoozeMachine.MachineId, id);
        row.Should().NotBeNull();
        SameInstant(DueAt(started), row, "start answers the due time it armed");
        (await StoredPatience(id)).Should().Be("PT5M", "the context stores what the engine reads");

        var read = await Get(SnoozeMachine.MachineId, "snooze", id, Fields);
        read.GetProperty("context").GetProperty("patience").GetString().Should().Be("PT5M");
        SameInstant(DueAt(read), row, "get answers the row's due time");
    }

    [Test]
    public async Task An_event_carrying_a_duration_arms_the_timer_it_enters()
    {
        var started = await Snooze(
            $"mutation {{ machines {{ snooze {{ start(input: {{ patience: \"PT5M\" }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ {Fields} }} }} }} }}",
            "start"
        );
        var id = started.GetProperty("id").GetGuid();
        var woken = await Snooze(SnoozeEvent("wake", id, input: null), "wake");
        DueAt(woken).Should().BeNull("Awake has no timer");

        var snoozed = await Snooze(SnoozeEvent("snooze", id, "{ patience: \"PT10M\" }"), "snooze");
        Problem(snoozed).Should().BeNull();
        snoozed.GetProperty("context").GetProperty("patience").GetString().Should().Be("PT10M");
        SameInstant(
            DueAt(snoozed),
            await RowDueAt(SnoozeMachine.MachineId, id),
            "the event answers the due time it armed"
        );
        DueAt(snoozed)!
            .Value.Should()
            .BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task A_duration_outside_the_timers_bounds_is_invalid_input()
    {
        var refused = await Snooze(
            $"mutation {{ machines {{ snooze {{ start(input: {{ patience: \"PT2H\" }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ {Fields} }} }} }} }}",
            "start"
        );
        Problem(refused).Should().Be("invalid-input");
    }

    #endregion

    private const string Old = WizardOps.ResultFields;

    private static string SnoozeEvent(string field, Guid id, string? input) =>
        $"mutation {{ machines {{ snooze {{ {field}(id: {WizardOps.Literal(id.ToString())}{(input is null ? "" : $", input: {input}")}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ {Fields} }} }} }} }}";

    private Task<JsonElement> Wizard(string document, string field) =>
        Answer(document, "wizard", field);

    private Task<JsonElement> Snooze(string document, string field) =>
        Answer(document, "snooze", field);

    private async Task<JsonElement> Answer(string document, string machine, string field)
    {
        using var doc = await _host.PostAsync(document, apiKey: E2EHost.AliceKey);
        WizardOps.FailOnErrors(doc, field);
        var answer = doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty(machine)
            .GetProperty(field)
            .Clone();
        if (machine == "wizard" && Problem(answer) is { } code)
            Assert.Fail($"{field} was refused: {code}");
        return answer;
    }

    private async Task<JsonElement> Get(string machine, string field, Guid id, string fields)
    {
        using var doc = await _host.PostAsync(
            $"{{ machines {{ {field} {{ get(id: {WizardOps.Literal(id.ToString())}) {{ {fields} }} }} }} }}",
            apiKey: E2EHost.AliceKey
        );
        WizardOps.FailOnErrors(doc, "get");
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty(field)
            .GetProperty("get")
            .Clone();
    }

    private static async Task<JsonElement> NextAtLeast(GraphQLWs ws, long revision)
    {
        while (true)
        {
            var change = (await ws.NextDataAsync("1")).GetProperty("machineChanged").Clone();
            if (change.GetProperty("revision").GetInt64() >= revision)
                return change;
        }
    }

    private static bool Known(JsonElement answer) => answer.GetProperty("dueAtKnown").GetBoolean();

    private static DateTimeOffset? DueAt(JsonElement answer) =>
        answer.GetProperty("dueAt") is { ValueKind: JsonValueKind.String } due
            ? DateTimeOffset.Parse(due.GetString()!, CultureInfo.InvariantCulture)
            : null;

    // The answer's due time is the row's, to the millisecond the DateTime scalar writes.
    private static void SameInstant(DateTimeOffset? actual, DateTimeOffset? row, string because)
    {
        row.Should().NotBeNull();
        actual.Should().NotBeNull(because);
        actual!.Value.Should().BeCloseTo(row!.Value, TimeSpan.FromMilliseconds(1), because);
    }

    private static string? Problem(JsonElement answer) =>
        answer.TryGetProperty("problem", out var problem)
        && problem.ValueKind == JsonValueKind.Object
            ? problem.GetProperty("code").GetString()
            : null;

    private static async Task<DateTimeOffset?> RowDueAt(string machine, Guid id)
    {
        var due = await E2ESql.ScalarAsync<DateTime?>(
            Database,
            "SELECT due_at FROM trax.snapshot_draft WHERE machine = @machine AND id = @id",
            ("machine", machine),
            ("id", id)
        );
        return due is { } value
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : null;
    }

    private static Task<string?> StoredPatience(Guid id) =>
        E2ESql.ScalarAsync<string>(
            Database,
            "SELECT context->>'patience' FROM trax.snapshot_draft WHERE machine = 'snooze' AND id = @id",
            ("id", id)
        );
}
