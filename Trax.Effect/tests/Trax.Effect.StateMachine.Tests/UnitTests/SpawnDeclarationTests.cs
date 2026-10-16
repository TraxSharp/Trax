using System.Text.Json.Nodes;
using AwesomeAssertions;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A state that spawns a child machine, the pure half: the builder records it as an invoking state whose outcomes
/// come from the child, exports those outcomes as it exports a train's, and records the states a child ends in. See
/// <c>Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md</c>.
/// </summary>
public class SpawnDeclarationTests
{
    private enum Order
    {
        Open,
        Fulfilling,
        Shipped,
        Lost,
        Recalled,
    }

    private enum OrderTrigger
    {
        Fulfil,
    }

    private enum Line
    {
        Picking,
        Picked,
        Missing,
    }

    private enum LineTrigger
    {
        Pick,
        Miss,
    }

    private sealed record OrderContext
    {
        public Guid? LineId { get; init; }

        public string Bin { get; init; } = "";
    }

    private sealed record LineResult
    {
        public string Bin { get; init; } = "";
    }

    // Stands in for the child machine type; the engine only records it, persistence resolves it.
    private sealed class LineMachine;

    private static MachineBuilder<Order, OrderTrigger> Parent(
        Action<IInvokeBuilder<Order, OrderTrigger>> outcomes
    )
    {
        var m = new MachineBuilder<Order, OrderTrigger>();
        m.Id("order")
            .StartsAt(Order.Open, () => new JsonObject { ["lineId"] = null, ["bin"] = "" });
        m.In(Order.Open).Context<OrderContext>().On(OrderTrigger.Fulfil).To(Order.Fulfilling);
        outcomes(
            m.In(Order.Fulfilling)
                .Context<OrderContext>()
                .Spawns<LineMachine, OrderContext, LineResult>(c => c.LineId)
        );
        m.In(Order.Shipped).Context<OrderContext>();
        m.In(Order.Lost).Context<OrderContext>();
        m.In(Order.Recalled).Context<OrderContext>();
        return m;
    }

    private static BuiltMachine<Order, OrderTrigger> Built() =>
        Parent(i =>
                i.OnDone(
                        Order.Shipped,
                        reduce: Set((OrderContext c) => c.Bin).FromInput((LineResult r) => r.Bin)
                    )
                    .OnFailed(Order.Lost)
                    .OnCancelled(Order.Recalled)
            )
            .Build();

    [Test]
    public void A_spawning_state_is_an_invoking_state_whose_outcomes_come_from_the_child()
    {
        var spawn = Built().Definition.Invokes[Order.Fulfilling];

        spawn.Spawn.Should().NotBeNull();
        spawn.Spawn!.ChildMachine.Should().Be(typeof(LineMachine));
        spawn.Spawn.ChildIdField.Should().Be("lineId");
    }

    [Test]
    public void The_childs_end_moves_the_parent_as_a_runs_output_does()
    {
        var built = Built();
        var current = new Snapshot
        {
            Machine = "order",
            Version = 1,
            State = nameof(Order.Fulfilling),
            Context = new JsonObject { ["lineId"] = Guid.NewGuid().ToString(), ["bin"] = "" },
        };

        var moved = built
            .Engine.ApplyOutcome(current, new InvokeOutcome.Done(new JsonObject { ["bin"] = "B7" }))
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Subject.Snapshot;

        moved.State.Should().Be(nameof(Order.Shipped));
        moved.Context["bin"]!.GetValue<string>().Should().Be("B7");
    }

    [Test]
    public void Its_outcomes_export_as_a_trains_do()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(Built()))!;

        var outcomes = ir["outcomes"]!.AsObject();
        outcomes
            .Select(o => o.Key)
            .Should()
            .BeEquivalentTo("Fulfilling.done", "Fulfilling.failed", "Fulfilling.cancelled");
        outcomes["Fulfilling.done"]!["output"]!["fields"]![0]!["name"]!
            .GetValue<string>()
            .Should()
            .Be("bin");
    }

    [Test]
    public void A_spawning_state_without_OnCancelled_is_refused_naming_the_child()
    {
        var build = () => Parent(i => i.OnDone(Order.Shipped).OnFailed(Order.Lost)).Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fulfilling spawns LineMachine*OnCancelled*");
    }

    [Test]
    public void A_child_id_field_the_state_does_not_hold_is_refused()
    {
        var m = new MachineBuilder<Order, OrderTrigger>();
        m.Id("order").StartsAt(Order.Open, () => new JsonObject());
        m.In(Order.Open).On(OrderTrigger.Fulfil).To(Order.Fulfilling);
        m.In(Order.Fulfilling)
            .Context<LineResult>()
            .Spawns<LineMachine, OrderContext, LineResult>(c => c.LineId)
            .OnDone(Order.Shipped)
            .OnFailed(Order.Lost)
            .OnCancelled(Order.Recalled);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fulfilling*lineId*its context*");
    }

    [Test]
    public void A_child_records_the_states_it_ends_and_fails_in()
    {
        var m = new MachineBuilder<Line, LineTrigger>();
        m.Id("line").StartsAt(Line.Picking, () => new JsonObject());
        m.In(Line.Picking)
            .On(LineTrigger.Pick)
            .To(Line.Picked)
            .On(LineTrigger.Miss)
            .To(Line.Missing);
        m.In(Line.Picked).Ends();
        m.In(Line.Missing).Fails();

        var ends = m.Build().Definition.Ends;

        ends.Should()
            .BeEquivalentTo(
                new Dictionary<Line, InvokeOutcomeKind>
                {
                    [Line.Picked] = InvokeOutcomeKind.Done,
                    [Line.Missing] = InvokeOutcomeKind.Failed,
                }
            );
    }

    [Test]
    public void A_state_that_both_ends_and_fails_is_refused()
    {
        var m = new MachineBuilder<Line, LineTrigger>();
        m.Id("line").StartsAt(Line.Picking, () => new JsonObject());
        m.In(Line.Picking).On(LineTrigger.Pick).To(Line.Picked);
        m.In(Line.Picked).Ends().Fails();

        var build = () => m.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*Picked*Ends*Fails*");
    }
}
