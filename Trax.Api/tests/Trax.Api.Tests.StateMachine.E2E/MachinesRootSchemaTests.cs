using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The schema of the <c>machines</c> root on a host that exposes <see cref="WizardMachine"/>: a <c>start</c> taking
/// the declared start input, a <c>discard</c>, one mutation per client event and a <c>get</c> query; the server's
/// revision beside the context, never in it; no input argument and no JSON scalar for an event declared without
/// <c>WithInput</c>; no field or argument for what only the server sends (outcomes, timers, an effect's receipt);
/// nothing for a machine the host does not name; and a misspelt event refused by validation before anything runs.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachinesRootSchemaTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machines_schema";

    private IHost _host = null!;
    private IRequestExecutor _executor = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        // The generic mutations are opt-in, so the host opts in to show the raw-delegate machines still served
        // through them beside the typed wizard.
        _host = await E2EHost.StartAsync(
            Database,
            new CountingCharge(),
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                Machines = E2EHostOptions.OptInToGeneric,
                Services = E2EHostOptions.AddGenericPolicy,
            }
        );
        _executor = await _host.ExecutorAsync();
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    private ObjectType? WizardMutations =>
        SchemaWalk.MachineType(_executor, SchemaWalk.RootMutation, WizardMachine.MachineId);

    private ObjectType? WizardQueries =>
        SchemaWalk.MachineType(_executor, SchemaWalk.RootQuery, WizardMachine.MachineId);

    private ObjectType RequireWizardMutations()
    {
        WizardMutations
            .Should()
            .NotBeNull("an exposed machine has its own field under Mutation.machines, per " + Adr);
        return WizardMutations!;
    }

    [Test]
    public void Machines_root_lists_each_exposed_machine_with_start_events_and_get()
    {
        var mutations = RequireWizardMutations();

        var start = mutations.Fields["start"];
        start
            .Arguments.Select(a => (a.Name, a.Type.Print()))
            .Should()
            .BeEquivalentTo(
                [("input", "WizardStartInput!"), ("requestId", "String!")],
                "start takes the declared start input and a request id, per " + Adr
            );
        mutations
            .Fields["discard"]
            .Arguments.Select(a => a.Name)
            .Should()
            .BeEquivalentTo(["id", "requestId"]);
        mutations
            .Fields.Select(f => f.Name)
            .Should()
            .Contain(WizardMachine.ClientEventFields, "one field per client event");

        WizardQueries
            .Should()
            .NotBeNull("an exposed machine has its own field under Query.machines");
        var get = WizardQueries!.Fields["get"];
        get.Arguments.Select(a => a.Name).Should().BeEquivalentTo(["id"]);

        var result = (ObjectType)start.Type.NamedType();
        result
            .Fields.Select(f => f.Name)
            .Should()
            .Contain(
                ["id", "revision", "state", "context"],
                "the server's revision travels beside the snapshot, per " + Adr
            );
        ((ObjectType)result.Fields["context"].Type.NamedType())
            .Fields.Select(f => f.Name)
            .Should()
            .NotContain("revision", "the revision is never inside the context");

        _executor
            .Schema.ToString()
            .Should()
            .Contain("start(input: WizardStartInput!, requestId: String!)");
    }

    [Test]
    public async Task An_unexposed_declarative_machine_gets_no_field()
    {
        var mutations = SchemaWalk.FieldType(
            SchemaWalk.ObjectTypeNamed(_executor, SchemaWalk.RootMutation),
            "machines"
        );
        mutations.Should().NotBeNull("the host exposes the wizard, so machines exists, per " + Adr);
        mutations!
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                [WizardMachine.MachineId],
                "the memo machine is declarative and registered, but not named in Expose, and the raw-delegate "
                    + "turnstile and order are never served typed"
            );

        // The raw-delegate machines keep working through the generic mutations.
        using var saved = await _host.PostAsync(
            """
            mutation Save($input: SaveSnapshotInput!) {
              dispatch { stateMachine { saveSnapshot(input: $input) { output { snapshot } } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = Guid.NewGuid().ToString(),
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            E2EHost.AdminApiKey
        );
        saved.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Test]
    public void An_event_without_WithInput_has_no_input_argument_and_no_json_scalar()
    {
        var mutations = RequireWizardMutations();

        mutations
            .Fields["approve"]
            .Arguments.Select(a => a.Name)
            .Should()
            .NotContain(
                "input",
                "approve declares no WithInput, so it takes no input, never a JSON scalar, per "
                    + Adr
            );

        foreach (
            var root in new[] { WizardMutations!, WizardQueries ?? WizardMutations! }.Distinct()
        )
            SchemaWalk
                .Reachable(root)
                .OfType<ScalarType>()
                .Select(s => s.Name)
                .Should()
                .NotContain(
                    ["JSON", "Any", "JsonElement"],
                    "no untyped scalar is reachable from machines, per " + Adr
                );
    }

    [Test]
    public void Outcome_timer_and_effect_receipt_inputs_never_appear_as_client_arguments()
    {
        var mutations = RequireWizardMutations();

        mutations
            .Fields.Where(f => !f.IsIntrospectionField)
            .Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                ["start", "discard", .. WizardMachine.ClientEventFields],
                "outcomes (done, failed, cancelled) and the Approving timer are server-only, per "
                    + Adr
            );
        mutations
            .Fields["pay"]
            .Arguments.Select(a => a.Name)
            .Should()
            .NotContain(
                ["input", "receipt"],
                "the effect produces the receipt; a client never sends it"
            );
    }

    [Test]
    public void Start_takes_its_declared_StartInput_never_the_context_type()
    {
        var mutations = RequireWizardMutations();

        var input = (InputObjectType)mutations.Fields["start"].Arguments["input"].Type.NamedType();

        input
            .RuntimeType.Should()
            .Be(typeof(WizardStartInput), "start takes its own declared record, per " + Adr);
        input
            .Fields.Select(f => f.Name)
            .Should()
            .BeEquivalentTo(
                ["title"],
                "a client cannot start an instance with the receipt or the artifact"
            );
    }

    [Test]
    public async Task A_misspelt_event_field_is_a_validation_error_not_a_runtime_problem()
    {
        var started = await _host.StartWizardAsync(E2EHost.AliceKey);
        var runsBefore = await E2ESql.CountMachineRunsAsync(Database);
        var rowBefore = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, started.Id);

        using var doc = await _host.PostAsync(
            WizardOps.Event(
                "descrbe",
                started.Id,
                WizardOps.NewRequestId(),
                WizardOps.DetailsInput()
            ),
            apiKey: E2EHost.AliceKey
        );

        WizardOps
            .FirstErrorMessage(doc)
            .Should()
            .Contain("descrbe", "an unknown event is refused by validation, per " + Adr);
        (await E2ESql.CountMachineRunsAsync(Database)).Should().Be(runsBefore, "nothing ran");
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, started.Id))
            .Should()
            .Be(rowBefore, "nothing was written");
    }
}
