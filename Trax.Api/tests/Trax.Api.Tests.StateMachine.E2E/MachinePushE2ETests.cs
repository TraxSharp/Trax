using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Each exposed machine's push is a subscription field of its own, <c>&lt;machine&gt;Changed(id)</c>, answering the
/// machine's own types: the state as the machine's state enum, the context as its context type, field for field what
/// <c>get</c> answers, and <c>dueAtKnown</c> beside <c>dueAt</c>. A gone or refused event carries no snapshot. An enum
/// a machine's context or inputs hold is spelled on the wire as the server stores it, the C# member name, so the twin
/// that applies an input as sent predicts what the server stores.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachinePushE2ETests
{
    private const string Database = "trax_sm_machine_push";

    private IHost _host = null!;
    private IRequestExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions { Served = [typeof(WizardMachine), typeof(TicketMachine)] }
        );
        _executor = await _host.ExecutorAsync();
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    #region Schema

    [Test]
    public void Each_served_machine_has_its_own_push_and_there_is_no_generic_one()
    {
        var subscriptions = _executor.Schema.SubscriptionType!;

        subscriptions.Fields.TryGetField("machineChanged", out _).Should().BeFalse();

        var wizard = subscriptions.Fields["wizardChanged"];
        wizard.Arguments.Select(a => (a.Name, a.Type.Print())).Should().Equal(("id", "UUID!"));

        var change = (ObjectType)wizard.Type.NamedType();
        change.Fields["state"].Type.Print().Should().Be("WizardMachineState");
        change.Fields["context"].Type.Print().Should().Be("WizardMachineContext");
        change.Fields["dueAtKnown"].Type.Print().Should().Be("Boolean!");
        change.Fields["problem"].Type.Print().Should().Be("SnapshotProblem");

        subscriptions.Fields.TryGetField("ticketChanged", out _).Should().BeTrue();
    }

    [Test]
    public void An_enum_a_machine_holds_has_its_stored_spelling_in_the_schema()
    {
        var priority = (EnumType)_executor.Schema.Types[nameof(TicketPriority)];

        priority
            .Values.Select(v => v.Name)
            .Should()
            .Equal(["Low", "HighPriority"], "the server stores an enum by its member name");
    }

    #endregion

    #region Push

    [Test]
    public async Task Push_and_get_answer_the_same_fields()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", Subscribe("wizard", started.Id, WizardFields));
        await ws.NextDataAsync("1");

        await _host.PostAsync(
            WizardOps.Describe(started.Id, WizardOps.NewRequestId(), note: "short"),
            apiKey: E2EHost.AliceKey
        );
        var pushed = Changed(await ws.NextDataAsync("1"), "wizard");
        var read = await _host.GetWizardAsync(E2EHost.AliceKey, started.Id);

        pushed.GetProperty("state").GetString().Should().Be(read.GetProperty("state").GetString());
        pushed
            .GetProperty("context")
            .GetRawText()
            .Should()
            .Be(
                read.GetProperty("context").GetRawText(),
                "the push answers the get's context type"
            );
    }

    [Test]
    public async Task Push_carries_dueAtKnown()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", Subscribe("wizard", started.Id, "revision dueAt dueAtKnown"));

        var first = Changed(await ws.NextDataAsync("1"), "wizard");

        first
            .GetProperty("dueAtKnown")
            .GetBoolean()
            .Should()
            .BeTrue("a push is the row as it is now");
    }

    [Test]
    public async Task A_gone_push_carries_no_state_or_context()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", Subscribe("wizard", started.Id, "gone " + WizardFields));
        await ws.NextDataAsync("1");

        await _host.PostAsync(
            WizardOps.Discard(started.Id, WizardOps.NewRequestId()),
            apiKey: E2EHost.AliceKey
        );
        var gone = Changed(await ws.NextDataAsync("1"), "wizard");

        gone.GetProperty("gone").GetBoolean().Should().BeTrue();
        gone.GetProperty("state").ValueKind.Should().Be(JsonValueKind.Null);
        gone.GetProperty("context").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task Someone_elses_instance_is_not_found()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.BobKey);
        await ws.SubscribeAsync("1", Subscribe("wizard", started.Id, "problem { code }"));

        var refused = Changed(await ws.NextDataAsync("1"), "wizard");

        refused
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(RefusalCode.NotFound.ToWire());
    }

    #endregion

    #region Enums

    [Test]
    public async Task An_enum_travels_in_its_stored_spelling_in_and_out()
    {
        var id = await StartTicketAsync();
        await using var ws = await GraphQLWs.ConnectAsync(_host, E2EHost.AliceKey);
        await ws.SubscribeAsync("1", Subscribe("ticket", id, "state context { priority }"));
        await ws.NextDataAsync("1");

        using var triaged = await _host.PostAsync(
            Triage(id, "HighPriority"),
            apiKey: E2EHost.AliceKey
        );
        var answer = Ticket(triaged, "triage");
        var pushed = Changed(await ws.NextDataAsync("1"), "ticket");

        answer
            .GetProperty("context")
            .GetProperty("priority")
            .GetString()
            .Should()
            .Be("HighPriority");
        pushed
            .GetProperty("context")
            .GetProperty("priority")
            .GetString()
            .Should()
            .Be("HighPriority");
        (await StoredContextAsync(id)).Should().Contain("\"HighPriority\"");
    }

    [Test]
    public async Task An_enum_input_predicted_by_the_twin_is_not_divergence()
    {
        var id = await StartTicketAsync();
        // The twin applies the input as the client sends it, so its prediction holds the value as sent.
        var predicted =
            "{\"machine\":\"ticket\",\"version\":1,\"state\":\"Triaged\",\"context\":{\"priority\":\"HighPriority\",\"title\":\"Leak\"}}";

        using var triaged = await _host.PostAsync(
            Triage(id, "HighPriority", predicted),
            apiKey: E2EHost.AliceKey
        );
        var answer = Ticket(triaged, "triage");

        answer.GetProperty("problem").ValueKind.Should().Be(JsonValueKind.Null);
        answer.GetProperty("state").GetString().Should().Be(nameof(TicketState.Triaged));
    }

    #endregion

    private const string WizardFields =
        "revision state context { title pages note receipt artifact } dueAt dueAtKnown problem { code }";

    private static string Subscribe(string field, Guid id, string selection) =>
        $"subscription {{ {field}Changed(id: {WizardOps.Literal(id.ToString())}) {{ {selection} }} }}";

    private static JsonElement Changed(JsonElement data, string field) =>
        data.GetProperty($"{field}Changed").Clone();

    private async Task<Guid> StartTicketAsync()
    {
        using var doc = await _host.PostAsync(
            $"mutation {{ machines {{ ticket {{ start(input: {{ title: \"Leak\" }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}) {{ id problem {{ code }} }} }} }} }}",
            apiKey: E2EHost.AliceKey
        );
        return Ticket(doc, "start").GetProperty("id").GetGuid();
    }

    private static string Triage(Guid id, string priority, string? clientResult = null) =>
        $"mutation {{ machines {{ ticket {{ triage(id: {WizardOps.Literal(id.ToString())}, input: {{ priority: {priority} }}, requestId: {WizardOps.Literal(WizardOps.NewRequestId())}"
        + (clientResult is null ? "" : $", clientResult: {WizardOps.Literal(clientResult)}")
        + ") { state context { priority } problem { code message } } } } }";

    private static JsonElement Ticket(JsonDocument doc, string field)
    {
        WizardOps.FailOnErrors(doc, field);
        return doc
            .RootElement.GetProperty("data")
            .GetProperty("machines")
            .GetProperty("ticket")
            .GetProperty(field)
            .Clone();
    }

    private static async Task<string> StoredContextAsync(Guid id) =>
        (await E2ESql.DraftAsync(Database, TicketMachine.MachineId, id))!.Context;
}
