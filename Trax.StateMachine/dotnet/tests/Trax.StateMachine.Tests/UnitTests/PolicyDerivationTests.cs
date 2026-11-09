using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A Then that moves another instance after a When is a policy (ADR <c>Trax.Docs/adr/0058</c>): the server fires a
/// trigger only it fires, of a shape the owners decide. A user's machine reaches another of the same owner's through a
/// link in the glossary, a system machine reaches every waiting instance, and anything reaches a system machine at its
/// key. What a policy cannot be (another owner's instance, no link, a trigger a person also fires) is a problem.
/// </summary>
public class PolicyDerivationTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed", "Expired": "has expired", "Baking": "is baking" },
              "triggers": {
                "Place": { "phrase": "places {subject}" },
                "Expire": { "phrase": "lets {subject} expire" },
                "Bake": { "phrase": "{subject} starts baking" }
              }
            },
            "receipt": {
              "ownedBy": "customer",
              "noun": "receipt",
              "start": "has a receipt",
              "initial": "Issued",
              "context": [ { "name": "orderId", "type": "string", "nullable": true, "format": "uuid", "constraints": [] } ],
              "states": { "Issued": "is issued", "Void": "is void" },
              "triggers": { "Cancel": { "phrase": "{subject} is voided" } },
              "links": { "the receipt for {subject}": { "machine": "order", "field": "orderId" } }
            },
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open" },
              "triggers": { "Open": { "phrase": "the shop opens" } }
            }
          }
        }
        """;

    private const string Background = """
        Feature: Bakery
          Background:
            Given Ada is a customer
            And Bo is a customer
            And Ada has an order
            And Ada has a receipt

          Scenario: A receipt is voided
            When Ada's receipt is voided
            Then Ada's receipt is void

          Scenario: An order starts baking
            Given Ada's order is placed
            When Ada's order starts baking
            Then Ada's order is baking

        """;

    private const string Expiring = """
          Scenario: An expired order voids its receipt
            Given Ada's order is placed
            When Ada lets her order expire
            Then Ada's order has expired
            And Ada's receipt is void
        """;

    [Test]
    public void One_users_machine_moving_another_of_theirs_is_a_linked_policy_through_the_glossarys_link()
    {
        var derived = Derive(Background + Expiring);

        derived.Problems.Should().BeEmpty();
        var policy = HostFile.Read(derived.Host!).Policies.Should().ContainSingle().Subject;
        policy.Shape.Should().Be("linked");
        (policy.When, policy.Enters, policy.Target, policy.Trigger, policy.By)
            .Should()
            .Be(("order", "Expired", "receipt", "Cancel", "orderId"));
        HostFile
            .Read(derived.Host!)
            .Machines["receipt"]
            .Triggers["Cancel"]
            .IsServer.Should()
            .BeTrue();
    }

    [Test]
    public void A_system_machine_moving_users_instances_is_a_broadcast_to_those_waiting()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: The shop opening starts the waiting orders
                    Given the shop is closed
                    And Ada's order is placed
                    When the shop opens
                    Then the shop is open
                    And Ada's order is baking
                """
        );

        derived.Problems.Should().BeEmpty();
        var policy = HostFile.Read(derived.Host!).Policies.Should().ContainSingle().Subject;
        (policy.Shape, policy.When, policy.Enters, policy.Target, policy.By, policy.Trigger)
            .Should()
            .Be(("broadcast", "shop", "Open", "order", "Placed", "Bake"));
    }

    [Test]
    public void A_policy_into_a_state_of_a_users_machine_that_runs_a_train_is_refused()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        var order = glossary["machines"]!["order"]!.AsObject();
        order["states"]!["Baked"] = "is baked";
        order["states"]!["Burnt"] = "is burnt";
        order["invokes"] = JsonNode.Parse(
            """
            {
              "Baking": {
                "train": "Shop.IBake",
                "outcomes": {
                  "the bread is done": { "outcome": "done" },
                  "the oven fails": { "outcome": "failed" },
                  "the baking is called off": { "outcome": "cancelled" }
                }
              }
            }
            """
        );
        static string Outcome(string said, string then) =>
            $"""

                  Scenario: {said}
                    Given Ada's order is placed
                    When Ada's order starts baking
                    Then Ada's order is baking
                    When {said}
                    Then Ada's order {then}

                """;

        var derived = Derive(
            Background
                + Outcome("the bread is done", "is baked")
                + Outcome("the oven fails", "is burnt")
                + Outcome("the baking is called off", "is burnt")
                + """
                  Scenario: The shop opening starts the waiting orders
                    Given the shop is closed
                    And Ada's order is placed
                    When the shop opens
                    Then the shop is open
                    And Ada's order is baking
                """,
            glossary.ToJsonString()
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("Baking, which runs a train");
    }

    [Test]
    public void A_derived_directory_with_policies_is_served_whole()
    {
        var derived = Derive(Background + Expiring);
        var directory = Directory.CreateTempSubdirectory("trax-derived-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "host.json"), derived.Host!);
            foreach (var (id, ir) in derived.Irs)
                File.WriteAllText(Path.Combine(directory, $"{id}.ir.json"), ir);

            var loaded = IrDirectory.Load(directory, []);

            loaded.Machines.Select(m => m.Name).Should().BeEquivalentTo(derived.Irs.Keys);
            loaded.Host.Policies.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Another_owners_instance_moving_is_refused()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Ada's order voids Bo's receipt
                    Given Bo has a receipt
                    And Ada's order is placed
                    When Ada lets her order expire
                    Then Ada's order has expired
                    And Bo's receipt is void
                """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("A policy acts on the same owner's instances");
    }

    [Test]
    public void The_same_owner_without_a_link_is_refused_as_ambiguous()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        glossary["machines"]!["receipt"]!.AsObject().Remove("links");

        var derived = Derive(Background + Expiring, glossary.ToJsonString());

        derived.Problems.Should().ContainSingle().Which.Message.Should().Contain("Add a link");
    }

    [Test]
    public void A_move_no_scenario_shows_a_trigger_for_is_a_problem_asking_for_one()
    {
        var derived = Derive(
            """
            Feature: Bakery
              Scenario: An expired order voids its receipt
                Given Ada is a customer
                And Ada has an order
                And Ada has a receipt
                And Ada's order is placed
                When Ada lets her order expire
                Then Ada's order has expired
                And Ada's receipt is void
            """
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("no scenario shows a trigger doing that");
    }

    [Test]
    public void A_When_that_moved_nothing_sets_nothing_off()
    {
        var derived = Derive(
            Background
                + """
                  Scenario: Expiring a draft
                    When Ada lets her order expire
                    Then Ada's order is still a draft
                    And Ada's receipt is void
                """
        );

        derived
            .Problems.Should()
            .Contain(p => p.Message.Contains("the When before it moved nothing"));
    }

    // Two system machines: closing the shop rings the bell, and the bell ringing moves the shop. Close and Wake are
    // the shop's triggers.
    private const string Bells = """
        {
          "version": 1,
          "actors": { "customer": { "noun": "a customer", "roles": ["customer"] } },
          "machines": {
            "shop": {
              "system": "shop/main",
              "name": "the shop",
              "initial": "Closed",
              "states": { "Closed": "is closed", "Open": "is open", "Idle": "is idle" },
              "triggers": {
                "Close": { "phrase": "the shop closes" },
                "Wake": { "phrase": "the shop wakes" }
              }
            },
            "bell": {
              "system": "bell/main",
              "name": "the bell",
              "initial": "Quiet",
              "states": { "Quiet": "is quiet", "Ringing": "is ringing" },
              "triggers": { "Ring": { "phrase": "the bell rings" } }
            }
          }
        }
        """;

    private const string Ringing = """
        Feature: The bell
          Scenario: Closing the shop rings the bell
            Given the shop is open
            And the bell is quiet
            When the shop closes
            Then the shop is closed
            And the bell is ringing

        """;

    [Test]
    public void Two_policies_that_set_each_other_off_are_refused_naming_both()
    {
        // The bell ringing closes the shop, and the shop closing rings the bell.
        var derived = Derive(
            Ringing
                + """
                  Scenario: The bell closes the shop
                    Given the shop is open
                    And the bell is quiet
                    When the bell rings
                    Then the bell is ringing
                    And the shop is closed
                """,
            Bells
        );

        derived
            .Problems.Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain(
                "The policies bell.Ringing>shop.Close and shop.Closed>bell.Ring set each other off"
            );
    }

    [Test]
    public void A_policy_loop_a_timer_closes_is_derived_and_served()
    {
        // Closing rings the bell, the bell wakes an idle shop, and the open shop closes after hours: the timer paces
        // it, as a daily open and close.
        var derived = Derive(
            Ringing
                + """
                  Scenario: The bell wakes the shop
                    Given the shop is idle
                    And the bell is quiet
                    When the bell rings
                    Then the bell is ringing
                    And the shop is open

                  Scenario: The shop wakes
                    Given the shop is idle
                    When the shop wakes
                    Then the shop is open

                  Scenario: The shop is still open before closing time
                    Given the shop is open
                    When 3 seconds pass
                    Then the shop is still open

                  Scenario: The shop closes after hours
                    Given the shop is open
                    When 5 seconds pass
                    Then the shop is closed
                """,
            Bells
        );

        derived.Problems.Should().BeEmpty();
        HostFile
            .Read(derived.Host!)
            .Policies.Select(p => p.Id)
            .Should()
            .BeEquivalentTo(["bell.Ringing>shop.Wake", "shop.Closed>bell.Ring"]);
        var directory = Directory.CreateTempSubdirectory("trax-derived-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "host.json"), derived.Host!);
            foreach (var (id, ir) in derived.Irs)
                File.WriteAllText(Path.Combine(directory, $"{id}.ir.json"), ir);

            IrDirectory.Load(directory, []).Host.Policies.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A live quiz: entering Scoring closes every open answer sheet and runs a scoring train, and the train's outcome
    // takes the quiz to Between, which clears every closed sheet. The second broadcast waits for the first's result.
    private const string Quiz = """
        {
          "version": 1,
          "actors": { "player": { "noun": "a player", "roles": ["player"] } },
          "machines": {
            "quiz": {
              "system": "quiz/main",
              "name": "the quiz",
              "initial": "Waiting",
              "states": {
                "Waiting": "is waiting for the first question",
                "Open": "is taking answers",
                "Scoring": "is scoring the question",
                "Between": "is between questions"
              },
              "triggers": {
                "Ask": { "phrase": "the quiz opens a question" },
                "Stop": { "phrase": "the question closes" }
              },
              "invokes": {
                "Scoring": {
                  "train": "Quiz.IScoreQuestion",
                  "outcomes": {
                    "the answers are scored": { "outcome": "done" },
                    "the scoring fails": { "outcome": "failed" },
                    "the scoring is called off": { "outcome": "cancelled" }
                  }
                }
              }
            },
            "answer-sheet": {
              "ownedBy": "player",
              "noun": "answer sheet",
              "start": "has an answer sheet",
              "initial": "Waiting",
              "states": { "Waiting": "is waiting for a question", "Open": "is open", "Closed": "is closed" },
              "triggers": {
                "Open": { "phrase": "{subject} opens" },
                "Close": { "phrase": "{subject} closes" },
                "Clear": { "phrase": "{subject} is cleared" }
              }
            }
          }
        }
        """;

    private const string Sheets = """
        Feature: The quiz
          Background:
            Given Ada is a player
            And Ada has an answer sheet

          Scenario: A sheet opens
            When Ada's answer sheet opens
            Then Ada's answer sheet is open

          Scenario: A sheet closes
            Given Ada's answer sheet is open
            When Ada's answer sheet closes
            Then Ada's answer sheet is closed

          Scenario: A sheet is cleared
            Given Ada's answer sheet is closed
            When Ada's answer sheet is cleared
            Then Ada's answer sheet is waiting for a question

          Scenario: The quiz opens its first question
            Given the quiz is waiting for the first question
            When the quiz opens a question
            Then the quiz is taking answers

          Scenario: The quiz opens the next question
            Given the quiz is between questions
            When the quiz opens a question
            Then the quiz is taking answers

          Scenario: The question closes, and the open sheets close with it
            Given Ada's answer sheet is open
            And the quiz is taking answers
            When the question closes
            Then the quiz is scoring the question
            And Ada's answer sheet is closed

          Scenario: Scoring that fails waits for the next question
            Given the quiz is scoring the question
            When the scoring fails
            Then the quiz is between questions

          Scenario: Scoring that is called off waits for the next question
            Given the quiz is scoring the question
            When the scoring is called off
            Then the quiz is between questions

        """;

    [Test]
    public void A_policy_waiting_on_another_policys_result_is_warned()
    {
        var derived = Derive(
            Sheets
                + """
                  Scenario: The answers are scored and the closed sheets are cleared
                    Given Ada's answer sheet is closed
                    And the quiz is scoring the question
                    When the answers are scored
                    Then the quiz is between questions
                    And Ada's answer sheet is waiting for a question
                """,
            Quiz
        );

        derived.Problems.Should().BeEmpty();
        derived.Host.Should().NotBeNull("a warning writes the files all the same");
        var warning = derived.Warnings.Should().ContainSingle().Subject;
        (warning.File, warning.Line).Should().Be(("bakery.feature", 51L));
        warning
            .Message.Should()
            .Be(
                "When quiz enters Between, the policy quiz.Between>answer-sheet.Clear moves answer-sheet instances in Closed; "
                    + "they reach Closed through quiz.Scoring>answer-sheet.Close, a policy set off when quiz enters Scoring, "
                    + "and on the host that policy may land after this one: each policy and each train runs on its own, in no fixed order. "
                    + "Don't let one policy wait for another's result: move them from a state the earlier policy leaves alone, or move them in one policy."
            );
    }

    [Test]
    public void A_policy_set_off_only_by_the_earlier_ones_landing_is_not_warned_and_a_train_beside_it_is()
    {
        // Closing a sheet tallies the quiz, and the tallied quiz clears the closed sheets: the clear comes after the
        // close by construction. A scoring train that also reaches Tallied from Scoring races the close.
        static Dictionary<string, JsonObject> Irs(bool train) =>
            new()
            {
                ["quiz"] = JsonNode
                    .Parse(
                        $$"""
                        {
                          "transitions": [ { "from": "Scoring", "to": "Tallied", "trigger": "Tally" } ],
                          "outcomes": { {{(
                            train
                                ? "\"done\": { \"state\": \"Scoring\", \"edges\": [ { \"to\": \"Tallied\" } ] }"
                                : ""
                        )}} }
                        }
                        """
                    )!
                    .AsObject(),
                ["sheet"] = JsonNode
                    .Parse(
                        """
                        {
                          "transitions": [
                            { "from": "Open", "to": "Closed", "trigger": "Close" },
                            { "from": "Closed", "to": "Waiting", "trigger": "Clear" }
                          ]
                        }
                        """
                    )!
                    .AsObject(),
            };
        HostPolicy[] policies =
        [
            new("broadcast", "quiz", "Scoring", "sheet", "Close", "Open"),
            new("toSystem", "sheet", "Closed", "quiz", "Tally", "quiz/main"),
            new("broadcast", "quiz", "Tallied", "sheet", "Clear", "Closed"),
        ];

        PolicyLoops.Races(policies, Irs(train: false)).Should().BeEmpty();
        PolicyLoops
            .Races(policies, Irs(train: true))
            .Select(r => (r.Later.Id, r.Earlier.Id, r.State))
            .Should()
            .Equal(("quiz.Tallied>sheet.Clear", "quiz.Scoring>sheet.Close", "Closed"));
    }

    [Test]
    public void A_policy_waiting_where_the_earlier_ones_train_moves_its_instance_is_warned()
    {
        // Scoring sends the open sheets to marking, whose train closes them, and a scoring train tallies the quiz,
        // which clears the closed sheets. The clear waits for the marking train, not for the policy itself.
        var irs = new Dictionary<string, JsonObject>
        {
            ["quiz"] = JsonNode
                .Parse(
                    """
                    {
                      "transitions": [],
                      "outcomes": { "done": { "state": "Scoring", "edges": [ { "to": "Tallied" } ] } }
                    }
                    """
                )!
                .AsObject(),
            ["sheet"] = JsonNode
                .Parse(
                    """
                    {
                      "transitions": [
                        { "from": "Open", "to": "Marking", "trigger": "Mark" },
                        { "from": "Closed", "to": "Waiting", "trigger": "Clear" }
                      ],
                      "outcomes": { "marked": { "state": "Marking", "edges": [ { "to": "Closed" } ] } }
                    }
                    """
                )!
                .AsObject(),
        };
        HostPolicy[] policies =
        [
            new("broadcast", "quiz", "Scoring", "sheet", "Mark", "Open"),
            new("broadcast", "quiz", "Tallied", "sheet", "Clear", "Closed"),
        ];

        PolicyLoops
            .Races(policies, irs)
            .Select(r => (r.Later.Id, r.Earlier.Id, r.State))
            .Should()
            .Equal(("quiz.Tallied>sheet.Clear", "quiz.Scoring>sheet.Mark", "Closed"));
    }

    [TestCase("PT0.5S", true)]
    [TestCase("PT0.999S", true)]
    [TestCase("PT1S", false)]
    [TestCase("PT2S", false)]
    [TestCase(null, false)]
    public void A_timer_between_two_policies_spaces_them_only_when_it_waits_the_runners_interval(
        string? after,
        bool warned
    )
    {
        // Scoring closes the open sheets, and a timer moves the quiz on to Tallied, which clears the closed ones. A
        // timer shorter than the policy runner's interval can fire before the close lands; one at least as long, or
        // one whose duration a context field holds, is a time a person chose.
        var timer = after is null ? """{ "name": "grace", "min": "PT0S", "max": "PT1H" }""" : null;
        var irs = new Dictionary<string, JsonObject>
        {
            ["quiz"] = JsonNode
                .Parse(
                    $$"""
                    {
                      "transitions": [],
                      "timers": [ { "state": "Scoring", "trigger": "Tally", "target": "Tallied", {{(
                        after is null ? $"\"field\": {timer}" : $"\"after\": \"{after}\""
                    )}} } ]
                    }
                    """
                )!
                .AsObject(),
            ["sheet"] = JsonNode
                .Parse(
                    """
                    {
                      "transitions": [
                        { "from": "Open", "to": "Closed", "trigger": "Close" },
                        { "from": "Closed", "to": "Waiting", "trigger": "Clear" }
                      ]
                    }
                    """
                )!
                .AsObject(),
        };
        HostPolicy[] policies =
        [
            new("broadcast", "quiz", "Scoring", "sheet", "Close", "Open"),
            new("broadcast", "quiz", "Tallied", "sheet", "Clear", "Closed"),
        ];

        var races = PolicyLoops.Races(policies, irs).Select(r => (r.Later.Id, r.Earlier.Id));

        if (warned)
            races.Should().Equal(("quiz.Tallied>sheet.Clear", "quiz.Scoring>sheet.Close"));
        else
            races.Should().BeEmpty();
    }

    [Test]
    public void The_public_derivation_returns_the_warnings()
    {
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Quiz),
            [
                new FeatureFile(
                    "bakery.feature",
                    Sheets
                        + """
                          Scenario: The answers are scored and the closed sheets are cleared
                            Given Ada's answer sheet is closed
                            And the quiz is scoring the question
                            When the answers are scored
                            Then the quiz is between questions
                            And Ada's answer sheet is waiting for a question
                        """
                ),
            ]
        );

        derived.Succeeded.Should().BeTrue("a warning stops nothing");
        var warning = derived.Warnings.Should().ContainSingle().Subject;
        (warning.File, warning.Line).Should().Be(("bakery.feature", 51L));
        warning
            .Message.Should()
            .StartWith(
                "When quiz enters Between, the policy quiz.Between>answer-sheet.Clear moves answer-sheet instances in Closed;"
            );
    }

    [Test]
    public void Policies_whose_order_the_host_keeps_are_not_warned()
    {
        // A cascade is ordered: the shop opens only once the shift's policy lands. A loop a timer closes is not a wait.
        Derive(Background + Expiring).Warnings.Should().BeEmpty();
        FeatureDerivation
            .Derive(
                Trax.StateMachine.Features.Glossary.Read(Bells),
                [
                    (
                        "bells.feature",
                        Ringing
                            + """
                              Scenario: The bell wakes the shop
                                Given the shop is idle
                                And the bell is quiet
                                When the bell rings
                                Then the bell is ringing
                                And the shop is open

                              Scenario: The shop wakes
                                Given the shop is idle
                                When the shop wakes
                                Then the shop is open

                              Scenario: The shop is still open before closing time
                                Given the shop is open
                                When 3 seconds pass
                                Then the shop is still open

                              Scenario: The shop closes after hours
                                Given the shop is open
                                When 5 seconds pass
                                Then the shop is closed
                            """
                    ),
                ]
            )
            .Warnings.Should()
            .BeEmpty();
    }

    private static Derived Derive(string feature, string glossary = Glossary) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("bakery.feature", feature)]
        );
}
