using AwesomeAssertions;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// What the glossary names and no scenario shows is a gap, and a gap's stub is green when pasted and changes only what
/// it claims (ADR <c>Trax.Docs/adr/0058</c>). The proof of concept offered stubs in the first person, which recorded new
/// access when pasted, and "guard fails" stubs that went red.
/// </summary>
public class GapStubTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "customer": { "noun": "a customer", "roles": ["customer"] },
            "manager": { "noun": "a manager", "roles": ["manager", "customer"] }
          },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "context": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
              "states": { "Draft": "is a draft", "Placed": "is placed", "Cancelled": "is cancelled" },
              "triggers": {
                "Order": {
                  "phrase": "orders {loaves} loaves for {subject}",
                  "input": [ { "name": "loaves", "type": "integer", "nullable": false, "constraints": [] } ],
                  "reduce": { "reduce": "set", "steps": [ { "field": "loaves", "value": { "input": "loaves" } } ] }
                },
                "Place": { "phrase": "places {subject}" },
                "Cancel": { "phrase": "cancels {subject}" }
              },
              "guards": {
                "hasLoaves": {
                  "holds": "has something in it",
                  "fails": "has nothing in it",
                  "rule": { "rule": "compare", "source": "context", "field": "loaves", "op": "gt", "value": 0 }
                }
              }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open" },
              "triggers": { "Open": { "phrase": "the shop opens" } }
            }
          },
          "views": {
            "orders": {
              "reads": [ { "machine": "order" } ],
              "fields": { "status": { "from": "order.state", "labels": { "Draft": "Not sent" } } }
            }
          }
        }
        """;

    private const string Ordering = """
        Feature: Ordering
          Background:
            Given Ada is a customer
            And Ada has an order

          Scenario: Ordering loaves
            When Ada orders "3" loaves for her order
            Then Ada's order is a draft

          Scenario: Placing an order with something in it
            Given Ada's order has something in it
            When Ada places her order
            Then Ada's order is placed
        """;

    [Test]
    public void Each_part_no_scenario_shows_is_a_gap()
    {
        var gaps = Find(Ordering);

        gaps.Select(g => (g.Kind, g.What))
            .Should()
            .BeEquivalentTo([
                (GapKind.Trigger, "Cancel"),
                (GapKind.State, "Cancelled"),
                (GapKind.Refusal, "Draft Place"),
                (GapKind.View, "orders"),
                (GapKind.Machine, "shop"),
                (GapKind.Grant, "order start"),
                (GapKind.Grant, "order read"),
                (GapKind.Grant, "order Order"),
                (GapKind.Grant, "order Place"),
            ]);
    }

    [Test]
    public void Every_stub_is_green_when_pasted_and_speaks_in_the_third_person()
    {
        var gaps = Find(Ordering).Where(g => g.Kind != GapKind.Grant).ToList();

        gaps.Should().OnlyContain(g => g.Stub != null);
        foreach (var gap in gaps)
        {
            gap.Stub.Should().NotMatchRegex(@"\b(I|my|me)\b");
            var derived = Derive(Ordering, gap.Stub);
            derived
                .Problems.Should()
                .BeEmpty($"the {gap.Kind} stub for {gap.What} is green:\n{gap.Stub}");
        }
    }

    [Test]
    public void A_pasted_stub_closes_its_gap_and_no_other()
    {
        var before = Find(Ordering);
        var cancel = before.Single(g => g.Kind == GapKind.Trigger);

        var after = Find(Ordering, cancel.Stub);

        after.Should().NotContain(g => g.Kind == GapKind.Trigger && g.What == "Cancel");
        after
            .Where(g => g.Kind is GapKind.Machine or GapKind.View or GapKind.Refusal)
            .Select(g => g.What)
            .Should()
            .BeEquivalentTo(["shop", "orders", "Draft Place"]);
    }

    [Test]
    public void The_refusal_stub_shows_the_guard_refusing_from_its_state()
    {
        var refusal = Find(Ordering).Single(g => g.Kind == GapKind.Refusal);

        refusal
            .Stub.Should()
            .Contain("And Ada's order has nothing in it")
            .And.Contain("Then Ada's order is still a draft");
    }

    [Test]
    public void Features_that_do_not_derive_have_their_problems_first()
    {
        var (gaps, problems) = Missing.Find(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            [("o.feature", "Feature: O\n  Scenario: S\n    When Ada juggles\n")]
        );

        gaps.Should().BeEmpty();
        problems.Should().NotBeEmpty();
    }

    // A stub is pasted as a feature of its own, so the Background of another does not repeat its Givens.
    private static IReadOnlyList<(string, string)> Files(string feature, string? stub) =>
        stub is null
            ? [("ordering.feature", feature)]
            : [("ordering.feature", feature), ("stub.feature", "Feature: Stub\n" + stub)];

    private static IReadOnlyList<Gap> Find(string feature, string? stub = null)
    {
        var (gaps, problems) = Missing.Find(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            Files(feature, stub)
        );
        problems.Should().BeEmpty();
        return gaps;
    }

    private static Derived Derive(string feature, string? stub = null) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(Glossary),
            Files(feature, stub)
        );
}
