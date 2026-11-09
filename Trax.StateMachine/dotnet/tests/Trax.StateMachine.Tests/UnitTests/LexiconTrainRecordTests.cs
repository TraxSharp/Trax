using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Tests.UnitTests.LexiconTrains;

namespace Trax.StateMachine.Tests.UnitTests
{
    /// <summary>
    /// Given the train assemblies, a lexicon reads each train's fields from its records, as the host does, so a renamed
    /// output field is caught at the lexicon line that names it and not first at the host's startup check (ADR
    /// <c>Trax.Docs/adr/0059</c>).
    /// </summary>
    public class LexiconTrainRecordTests
    {
        private const string Trains = "Trax.StateMachine.Tests.UnitTests.LexiconTrains";

        private static readonly string[] Wiring =
        [
            "    Input: square from target",
            "    Stores: shots, result as lastShot",
            "    Example: shots is \".\"",
        ];

        private static Gherkin.LexiconReading Read(string train, params string[] runs) =>
            Gherkin.Lexicon.Read(
                "game.lexicon",
                Text(train, runs),
                [typeof(IFireShotTrain).Assembly]
            );

        private static string Text(string train, params string[] runs) =>
            string.Join(
                "\n",
                [
                    "Version: 1",
                    $"Trains in: {Trains}",
                    "Actor: player \"a player\"",
                    "  Roles: User",
                    "Machine: game",
                    "  Owned by: player",
                    "  Context: shots is a string",
                    "  Context: target is a string",
                    "  State: Firing \"is firing\"",
                    $"  Runs: {train} in Firing",
                    .. runs,
                    "    Outcome: \"the shot misses\" when result is \"miss\"",
                    "    Outcome: \"the shot cannot be fired\" when it fails",
                ]
            );

        [Test]
        public void A_train_without_Returns_takes_its_output_and_input_from_its_records()
        {
            var reading = Read("FireShot", Wiring);

            reading.Problems.Should().BeEmpty();
            var invoke = JsonNode.Parse(reading.Json!)!["machines"]!["game"]!["invokes"]![
                "Firing"
            ]!;
            invoke["train"]!.GetValue<string>().Should().Be($"{Trains}.IFireShotTrain");
            invoke["output"]!
                .AsArray()
                .Select(f => f!["name"]!.GetValue<string>())
                .Should()
                .Equal("result", "shots");
            invoke["input"]!
                .AsObject()
                .Select(kv => (kv.Key, kv.Value!.GetValue<string>()))
                .Should()
                .Equal(("square", "target"), ("owner", "$owner"), ("shots", "shots"));
            invoke["examples"]!["the shot misses"]!
                .ToJsonString()
                .Should()
                .Be("""{"result":"miss","shots":"."}""");
        }

        [Test]
        public void A_renamed_output_field_is_a_problem_at_the_Stores_line_that_names_it()
        {
            var reading = Read("RenamedShot", Wiring);

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(12);
            reading.Problems[0].Message.Should().Contain("returns no 'result'");
        }

        [Test]
        public void A_Returns_line_that_disagrees_with_the_record_is_a_problem_at_its_line()
        {
            var reading = Read(
                "FireShot",
                ["    Returns: result is a boolean, shots is a string", .. Wiring]
            );

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(11);
            reading
                .Problems[0]
                .Message.Should()
                .Contain("returns 'result' as a string, not a boolean");
        }

        [Test]
        public void A_train_input_nothing_feeds_is_a_problem_at_its_Runs_line()
        {
            var reading = Read("FireShot", Wiring[1..]);

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(10);
            reading
                .Problems[0]
                .Message.Should()
                .Contain("'square' has no context field to come from");
        }

        [Test]
        public void A_Returns_line_that_disagrees_on_nullability_is_a_problem_at_its_line()
        {
            var reading = Read(
                "NotedShot",
                ["    Returns: result is a string, shots is a string, note is a string", .. Wiring]
            );

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(11);
            reading.Problems[0].Message.Should().Contain("'note'").And.Contain("may be empty");
        }

        [Test]
        public void A_Returns_line_that_agrees_with_the_record_gives_the_glossary_it_gives_without_it()
        {
            string[] runs =
            [
                "    Returns: result is a string, shots is a string",
                "    Takes: shots, owner",
                .. Wiring,
            ];
            var without = Gherkin.Lexicon.Read("game.lexicon", Text("FireShot", runs));

            Read("FireShot", runs).Json.Should().Be(without.Json).And.NotBeNull();
        }

        [Test]
        public void An_input_line_the_record_does_not_have_is_a_problem_at_its_line()
        {
            var reading = Read("FireShot", [.. Wiring, "    Input: aim from target"]);

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(14);
            reading.Problems[0].Message.Should().Contain("takes no 'aim'");
        }

        [Test]
        public void A_train_whose_types_do_not_load_is_a_problem_at_its_Runs_line()
        {
            var reading = Gherkin.Lexicon.Read(
                "game.lexicon",
                Text("FireShot", Wiring),
                [new UnloadableAssembly()]
            );

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(10);
            reading
                .Problems[0]
                .Message.Should()
                .Contain("could not be loaded")
                .And.Contain("Missing.Dependency");
        }

        // An assembly whose types reach a dependency that is not there, as a hub's bin missing a package is.
        private sealed class UnloadableAssembly : System.Reflection.Assembly
        {
            public override Type? GetType(string name, bool throwOnError) =>
                throw new FileNotFoundException(
                    "Could not load file or assembly 'Missing.Dependency'.",
                    "Missing.Dependency"
                );

            public override System.Reflection.AssemblyName GetName() => new("Unloadable");
        }

        [Test]
        public void A_train_the_assemblies_do_not_have_is_a_problem_at_its_Runs_line()
        {
            var reading = Read("NoSuch", Wiring);

            reading.Succeeded.Should().BeFalse();
            reading.Problems[0].Line.Should().Be(10);
            reading.Problems[0].Message.Should().Contain("no train");
        }
    }
}

namespace Trax.StateMachine.Tests.UnitTests.LexiconTrains
{
    using Trax.Effect.Services.ServiceTrain;

    public sealed record FireShotInput(string Square, string Shots, string Owner);

    public sealed record FireShotOutput(string Result, string Shots);

    public interface IFireShotTrain : IServiceTrain<FireShotInput, FireShotOutput>;

    public sealed record RenamedOutput(string Outcome, string Shots);

    public interface IRenamedShotTrain : IServiceTrain<FireShotInput, RenamedOutput>;

    public sealed record NotedOutput(string Result, string Shots, string? Note);

    public interface INotedShotTrain : IServiceTrain<FireShotInput, NotedOutput>;
}
