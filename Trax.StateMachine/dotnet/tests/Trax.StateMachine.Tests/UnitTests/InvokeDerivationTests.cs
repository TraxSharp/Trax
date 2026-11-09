using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// A state that invokes a train derives into what the host serves as written (ADR <c>Trax.Docs/adr/0058</c>): the
/// train's output in the order the host reads it from the train, and no outcome the host would refuse to run.
/// </summary>
public class InvokeDerivationTests
{
    // Output fields listed out of name order, as a reader writes them: most important first.
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "player": { "noun": "a player", "roles": ["player"] } },
          "machines": {
            "game": {
              "ownedBy": "player",
              "noun": "game",
              "start": "starts a game",
              "initial": "Ready",
              "states": {
                "Ready": "is ready",
                "Firing": "is firing",
                "Answering": "is being answered",
                "Fired": "has fired",
                "Stopped": "has stopped"
              },
              "triggers": { "Fire": { "phrase": "fires in {subject}" } },
              "invokes": {
                "Firing": {
                  "train": "Trax.StateMachine.Tests.UnitTests.IAimTrain",
                  "output": [
                    { "name": "shots", "type": "string", "nullable": false, "constraints": [] },
                    { "name": "result", "type": "string", "nullable": false, "constraints": [] }
                  ],
                  "outcomes": {
                    "the shot lands": { "outcome": "done" },
                    "the shot fails": { "outcome": "failed" },
                    "the shot is called off": { "outcome": "cancelled" }
                  },
                  "examples": { "the shot lands": { "shots": "x", "result": "hit" } }
                },
                "Answering": {
                  "train": "Trax.StateMachine.Tests.UnitTests.IAimTrain",
                  "output": [
                    { "name": "shots", "type": "string", "nullable": false, "constraints": [] },
                    { "name": "result", "type": "string", "nullable": false, "constraints": [] }
                  ],
                  "outcomes": {
                    "the answer lands": { "outcome": "done" },
                    "the answer fails": { "outcome": "failed" },
                    "the answer is called off": { "outcome": "cancelled" }
                  },
                  "examples": { "the answer lands": { "shots": "x", "result": "miss" } }
                }
              }
            }
          }
        }
        """;

    private const string Background = """
        Feature: Firing
          Background:
            Given Ada is a player
            And Ada starts a game

        """;

    private static string Shot(string outcome, string then) =>
        $"""
              Scenario: {outcome}
                When Ada fires in her game
                Then Ada's game is firing
                When {outcome}
                Then Ada's game {then}

            """;

    private static string Answer(string outcome, string then) =>
        $"""
              Scenario: {outcome}
                Given Ada's game is being answered
                When {outcome}
                Then Ada's game {then}

            """;

    [Test]
    public void Output_fields_out_of_name_order_derive_a_machine_the_host_serves_as_written()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        glossary["machines"]!["game"]!["states"]!.AsObject().Remove("Answering");
        glossary["machines"]!["game"]!["invokes"]!.AsObject().Remove("Answering");
        var derived = Derive(
            glossary.ToJsonString(),
            Background
                + Shot("the shot lands", "has fired")
                + Shot("the shot fails", "has stopped")
                + Shot("the shot is called off", "has stopped")
        );

        derived.Problems.Should().BeEmpty();
        var ir = JsonNode.Parse(derived.Irs["game"])!.AsObject();
        var host = HostFile.Read(derived.Host!);

        var served = IrMachines.Create(
            new IrMachines.Source(ir, host.Machines["game"]),
            [typeof(IAimTrain).Assembly]
        );

        served
            .ExportIr()
            .Should()
            .Be(
                derived.Irs["game"],
                "the host reads a train's output from its type, by name, and serves only the IR written"
            );
    }

    [Test]
    public void An_outcome_entering_another_invoking_state_on_a_users_machine_is_a_problem()
    {
        var derived = Derive(
            Glossary,
            Background
                + Shot("the shot lands", "is being answered")
                + Shot("the shot fails", "has stopped")
                + Shot("the shot is called off", "has stopped")
                + Answer("the answer lands", "has fired")
                + Answer("the answer fails", "has stopped")
                + Answer("the answer is called off", "has stopped")
        );

        var problem = derived
            .Problems.Should()
            .ContainSingle(
                "the host refuses a user's machine whose outcome starts another run with no user there"
            )
            .Subject;
        problem.File.Should().Be("game.feature");
        problem
            .Message.Should()
            .Contain("'the shot lands' takes game from Firing into Answering, which runs a train")
            .And.Contain("let a person's trigger enter Answering");
    }

    [Test]
    public void An_outcome_entering_another_invoking_state_on_a_system_machine_derives()
    {
        var glossary = JsonNode.Parse(Glossary)!;
        var game = glossary["machines"]!["game"]!.AsObject();
        game.Remove("ownedBy");
        game.Remove("noun");
        game.Remove("start");
        game["system"] = "game/main";
        game["name"] = "the game";
        game["triggers"] = JsonNode.Parse("""{ "Fire": { "phrase": "the game fires" } }""");

        var derived = Derive(
            glossary.ToJsonString(),
            """
            Feature: Firing
              Scenario: The shot is answered
                Given the game is ready
                When the game fires
                Then the game is firing
                When the shot lands
                Then the game is being answered
                When the answer lands
                Then the game has fired

              Scenario: The shot fails
                Given the game is firing
                When the shot fails
                Then the game has stopped

              Scenario: The shot is called off
                Given the game is firing
                When the shot is called off
                Then the game has stopped

              Scenario: The answer fails
                Given the game is being answered
                When the answer fails
                Then the game has stopped

              Scenario: The answer is called off
                Given the game is being answered
                When the answer is called off
                Then the game has stopped
            """
        );

        derived
            .Problems.Should()
            .NotContain(
                p => p.Message.Contains("another train's outcome"),
                "the server runs a system machine's trains as itself"
            );
    }

    private static Derived Derive(string glossary, string feature) =>
        FeatureDerivation.Derive(
            Trax.StateMachine.Features.Glossary.Read(glossary),
            [("game.feature", feature)]
        );
}

/// <summary>The input of the train the game's invoking states run.</summary>
public sealed record AimInput;

/// <summary>Its output, whose fields the host reads by name: <c>result</c> before <c>shots</c>.</summary>
public sealed record AimOutput(string Shots, string Result);

/// <summary>The train, named by its interface; deriving and serving the machine never runs it.</summary>
public interface IAimTrain : IServiceTrain<AimInput, AimOutput>;
