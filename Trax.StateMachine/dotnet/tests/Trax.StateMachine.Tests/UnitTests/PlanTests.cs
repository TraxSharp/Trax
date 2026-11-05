using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Fakes;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The planner (<c>conformance/PROTOCOL.md</c>, <c>plan</c>) over the kit's wizard and the test machines, beyond what
/// the kit's cases hold every engine to: the edge order an IR lists does not matter, a handler makes a custom edge
/// plannable, and the compiled machine's planner. The expansion cap is the kit's odometer cases.
/// </summary>
public class PlanTests
{
    private static readonly PlannedEvent Fill = new(
        "Update",
        new JsonObject { ["email"] = "ada@example.com", ["name"] = "Ada" }
    );

    private static readonly string[] Navigation = ["Begin", "Next", "Back"];

    private static JsonObject WizardIr() =>
        (JsonObject)
            JsonNode.Parse(
                File.ReadAllText(
                    Path.Combine(FixturePaths.ConformanceRoot, "plan", "wizard.ir.json")
                )
            )!;

    private static Snapshot Wizard(string state, string? name = null, string? email = null) =>
        new()
        {
            Machine = "wizard",
            Version = 1,
            State = state,
            Context = new JsonObject { ["email"] = email, ["name"] = name },
        };

    private static IEnumerable<string> Triggers(PlanResult result) =>
        result.Should().BeOfType<PlanResult.Planned>().Subject.Events.Select(e => e.Trigger);

    [Test]
    public void The_fields_event_is_tried_before_navigation_from_every_snapshot()
    {
        var plan = new IrMachine(WizardIr()).Plan(Wizard("Start"), "Review", Fill, Navigation);

        var events = plan.Should().BeOfType<PlanResult.Planned>().Subject.Events;
        events.Select(e => e.Trigger).Should().Equal("Begin", "Update", "Next");
        JsonNode.DeepEquals(events[1].Input, Fill.Input).Should().BeTrue();
        events[0].Input.Should().BeNull();
    }

    [Test]
    public void The_plan_does_not_depend_on_the_order_the_IR_lists_its_transitions()
    {
        var reversed = WizardIr();
        var transitions = reversed["transitions"]!.AsArray().Select(t => t!.DeepClone()).Reverse();
        reversed["transitions"] = new JsonArray(transitions.ToArray());

        var plan = new IrMachine(reversed).Plan(Wizard("Start"), "Review", Fill, Navigation);

        Triggers(plan).Should().Equal("Begin", "Update", "Next");
    }

    [Test]
    public void A_snapshot_at_the_target_holding_the_fields_plans_nothing()
    {
        var plan = new IrMachine(WizardIr()).Plan(
            Wizard("Review", "Ada", "ada@example.com"),
            "Review",
            Fill,
            Navigation
        );

        Triggers(plan).Should().BeEmpty();
    }

    [Test]
    public void An_edge_on_a_custom_guard_without_a_handler_is_not_tried_and_answers_undetermined()
    {
        var plan = new IrMachine(WizardIr()).Plan(Wizard("Details"), "Review", Fill, ["Skip"]);

        var undetermined = plan.Should().BeOfType<PlanResult.Undetermined>().Subject;
        undetermined.Trigger.Should().Be("Skip");
        undetermined.Events.Should().BeEmpty();
    }

    [Test]
    public void An_edge_on_a_custom_guard_with_a_handler_is_planned()
    {
        var machine = new IrMachine(
            WizardIr(),
            new Dictionary<string, Func<JsonObject, JsonNode?, bool>>
            {
                ["approved"] = (_, _) => true,
            }
        );

        var plan = machine.Plan(Wizard("Details"), "Review", Fill, ["Skip"]);

        Triggers(plan).Should().Equal("Update", "Skip");
    }

    // The wizard's search over a graph that counts what it asks of the engine.
    private static (PlanResult Plan, List<string> Advanced, int Serialized) Counted(Snapshot from)
    {
        var machine = new IrMachine(WizardIr());
        var advanced = new List<string>();
        var serialized = 0;
        var graph = new PlanGraph(
            4,
            s => s is "Start" or "Details" or "Review" or "Sent",
            t => t is "Begin" or "Next" or "Back" or "Update",
            (_, _) => true,
            (s, trigger, input) =>
            {
                // Read with the machine's own Serialize, which the count below does not see.
                advanced.Add($"{machine.Serialize(s)} {trigger}");
                return machine.Advance(s, trigger, input);
            },
            s =>
            {
                serialized++;
                return machine.Serialize(s);
            }
        );
        var plan = EventPlanner.Plan(graph, from, "Review", Fill, Navigation, null);
        return (plan, advanced, serialized);
    }

    [Test]
    public void Each_snapshot_is_advanced_by_each_edge_once_the_goal_tests_fields_event_included()
    {
        foreach (var from in new[] { Wizard("Start"), Wizard("Review", "Bob", "bob@example.com") })
        {
            var (plan, advanced, serialized) = Counted(from);

            plan.Should().BeOfType<PlanResult.Planned>();
            advanced.Should().OnlyHaveUniqueItems();
            // One wire for the snapshot the search starts from, and at most one for each step it took.
            serialized.Should().BeLessThanOrEqualTo(advanced.Count + 1);
        }
    }

    [Test]
    public void A_navigation_trigger_listed_twice_is_tried_once_from_each_snapshot()
    {
        var asked = 0;
        var machine = new IrMachine(
            WizardIr(),
            new Dictionary<string, Func<JsonObject, JsonNode?, bool>>
            {
                ["approved"] = (_, _) =>
                {
                    asked++;
                    return false;
                },
            }
        );

        var once = machine.Plan(Wizard("Details"), "Review", Fill, ["Skip"]);
        var onceAsked = asked;
        asked = 0;
        var thrice = machine.Plan(Wizard("Details"), "Review", Fill, ["Skip", "Skip", "Skip"]);

        thrice.Should().BeOfType<PlanResult.Unreachable>();
        once.Should().BeOfType<PlanResult.Unreachable>();
        asked.Should().Be(onceAsked);
    }

    [Test]
    public void A_target_past_the_depth_limit_is_exceeded_rather_than_unreachable()
    {
        var machine = new IrMachine(WizardIr());

        machine
            .Plan(Wizard("Start"), "Review", Fill, Navigation, maxDepth: 2)
            .Should()
            .BeOfType<PlanResult.Exceeded>();
        Triggers(machine.Plan(Wizard("Start"), "Review", Fill, Navigation, maxDepth: 3))
            .Should()
            .HaveCount(3);
    }

    [Test]
    public void A_trigger_a_client_may_not_move_with_is_invalid()
    {
        var machine = new IrMachine(WizardIr());
        var start = Wizard("Review");

        machine
            .Plan(start, "Nowhere", null, Navigation)
            .Should()
            .Be(new PlanResult.Invalid(Target: "Nowhere"));
        machine
            .Plan(start, "Sent", null, ["Next", "Send"])
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Send"));
        machine
            .Plan(start, "Details", null, ["Review.after"])
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Review.after"));
        machine
            .Plan(start, "Details", Fill, ["Begin", "Update"])
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Update"));
        machine
            .Plan(start, "Details", new PlannedEvent("Send"), Navigation)
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Send"));
    }

    [Test]
    public void The_compiled_machine_holds_every_handler_so_it_plans_what_the_IR_cannot()
    {
        var start = VaultMachine.Built.Definition.CreateInitialSnapshot();
        var ir = (JsonObject)JsonNode.Parse(File.ReadAllText(FixturePaths.IrFile("vault")))!;

        Triggers(VaultMachine.Built.Plan(start, "Sealed", null, ["Seal"])).Should().Equal("Seal");
        new IrMachine(ir)
            .Plan(start, "Sealed", null, ["Seal"])
            .Should()
            .BeOfType<PlanResult.Undetermined>()
            .Which.Trigger.Should()
            .Be("Seal");
    }

    [Test]
    public void The_compiled_machine_refuses_an_effect_trigger_as_navigation()
    {
        var start = DeclarativeCheckout.Built.Definition.CreateInitialSnapshot();

        DeclarativeCheckout
            .Built.Plan(start, "Paid", null, ["Next", "Pay"])
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Pay"));
    }

    public enum DoorState
    {
        Open,
        Shut,
    }

    public enum DoorTrigger
    {
        Close,
        Lock,
    }

    [Test]
    public void The_compiled_machine_refuses_a_trigger_no_transition_fires_on()
    {
        var m = new MachineBuilder<DoorState, DoorTrigger>();
        m.Id("door").Version(1).StartsAt(DoorState.Open, () => new JsonObject());
        m.In(DoorState.Open).On(DoorTrigger.Close).To(DoorState.Shut);
        var door = m.Build();

        door.Plan(door.Definition.CreateInitialSnapshot(), "Shut", null, ["Close", "Lock"])
            .Should()
            .Be(new PlanResult.Invalid(Trigger: "Lock"));
    }

    [Test]
    public void A_compiled_and_an_IR_machine_plan_alike()
    {
        var start = DeclarativeCheckout.Built.Definition.CreateInitialSnapshot();
        var ir = (JsonObject)JsonNode.Parse(File.ReadAllText(FixturePaths.IrFile("checkout")))!;

        var compiled = DeclarativeCheckout.Built.Plan(start, "Cart", null, ["Next", "Back"]);
        var fromIr = new IrMachine(ir).Plan(start, "Cart", null, ["Next", "Back"]);

        Triggers(compiled).Should().BeEmpty();
        Triggers(fromIr).Should().BeEmpty();
    }
}
