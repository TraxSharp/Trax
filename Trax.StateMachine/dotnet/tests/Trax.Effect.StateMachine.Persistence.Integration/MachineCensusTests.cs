using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The census lists every event and read of every machine with the rule that guards it and the surfaces that reach
/// it, so a host can review what clients and its own code may do: outcome and timer triggers are listed as the
/// server's own, and host code's reach is listed beside the clients'.
/// </summary>
[Property("adr", Adr)]
public class MachineCensusTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static IReadOnlyList<MachineCensusEntry> Census() =>
        MachineCensus.Describe([
            new RuledDeskMachine(),
            new RuledBuildMachine(),
            new RuledShipMachine(),
            new RuledSystemTurnstileMachine(),
        ]);

    private static MachineCensusEntry Entry(
        IReadOnlyList<MachineCensusEntry> census,
        string machine,
        string name
    ) =>
        census
            .Should()
            .ContainSingle(
                e => e.Machine == machine && e.Name == name,
                $"the census lists {machine}.{name} once. See {Adr}"
            )
            .Which;

    [Test]
    public void The_census_lists_every_event_and_read_with_its_rule_and_surfaces()
    {
        var census = Census();

        foreach (var trigger in Enum.GetNames<DeskTrigger>())
            Entry(census, RuledDeskMachine.Id, trigger)
                .Kind.Should()
                .Be(nameof(TriggerKind.Client));

        Entry(census, RuledDeskMachine.Id, nameof(DeskTrigger.OperatorOnly))
            .Rule.Should()
            .Contain(DeskRoles.Operator);
        Entry(census, RuledDeskMachine.Id, nameof(DeskTrigger.OperatorOrAdmin))
            .Rule.Should()
            .Contain(DeskRoles.Operator)
            .And.Contain(DeskRoles.Admin);
        Entry(census, RuledDeskMachine.Id, nameof(DeskTrigger.PolicyOnly))
            .Rule.Should()
            .Contain(DeskRoles.Policy);
        Entry(census, RuledDeskMachine.Id, nameof(DeskTrigger.Anyone))
            .Rule.Should()
            .NotContain(DeskRoles.Operator);

        foreach (var read in new[] { "start", "get", "changed" })
        {
            var entry = Entry(census, RuledDeskMachine.Id, read);
            entry.Kind.Should().Be("Read");
            entry.Rule.Should().NotBeNullOrWhiteSpace();
        }
        Entry(census, RuledDeskMachine.Id, "start").Rule.Should().Contain(DeskRoles.Operator);

        census
            .Where(e => e.Machine == RuledDeskMachine.Id && e.Kind != "Read")
            .Should()
            .AllSatisfy(e =>
                e.Surfaces.Should()
                    .NotBeEmpty($"every client event names the surfaces that reach it. See {Adr}")
            );

        // The effect-bound event is listed with its rule, though send names no trigger.
        var ship = Entry(census, RuledShipMachine.Id, nameof(ShipTrigger.Ship));
        ship.Kind.Should().Be(nameof(TriggerKind.EffectBound));
        ship.Rule.Should().Contain(DeskRoles.Operator);

        // Outcome triggers are the server's own: listed, never reachable by a client.
        var outcomes = census
            .Where(e => e.Machine == RuledBuildMachine.Id && e.Kind == nameof(TriggerKind.Outcome))
            .ToList();
        outcomes
            .Select(e => e.Name)
            .Should()
            .BeEquivalentTo(["Building.done", "Building.failed", "Building.cancelled"]);
        outcomes
            .Should()
            .AllSatisfy(e =>
                e.Rule.Should()
                    .ContainEquivalentOf(
                        "server",
                        $"an outcome is applied by the server only. See {Adr}"
                    )
            );
    }

    [Test]
    public void The_census_lists_discard_with_its_rule_and_surfaces()
    {
        var census = MachineCensus.Describe([new OperatorDiscardMachine()]);

        var discard = Entry(census, OperatorDiscardMachine.Id, "discard");
        discard.Kind.Should().Be("Discard");
        discard
            .Rule.Should()
            .Contain("OwnerOnly")
            .And.Contain(DeskRoles.Operator, $"the census names discard's rule. See {Adr}");
        discard
            .Surfaces.Should()
            .Contain(s => s.Contains("discard", StringComparison.Ordinal))
            .And.Contain(s => s.Contains("discardSnapshot", StringComparison.Ordinal));
    }

    [Test]
    public void The_census_lists_which_events_host_code_can_send()
    {
        var census = Census();

        foreach (var trigger in Enum.GetNames<TurnstileTrigger>())
            Entry(census, RuledSystemTurnstileMachine.Id, trigger)
                .Surfaces.Should()
                .Contain(
                    surface => surface.Contains("host", StringComparison.OrdinalIgnoreCase),
                    $"host code sends a system-owned machine's events, and the census says which. See {Adr}"
                );

        census
            .Where(e => e.Kind == nameof(TriggerKind.Outcome))
            .Should()
            .AllSatisfy(e =>
                e.Surfaces.Should()
                    .NotContain(surface =>
                        surface.Contains("host", StringComparison.OrdinalIgnoreCase)
                    )
            );
    }
}
