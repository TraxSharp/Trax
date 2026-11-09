using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Gherkin.Lexical;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The lexicon is what people write, and the glossary what Trax derives from it (ADR <c>Trax.Docs/adr/0059</c>; the grammar is
/// <c>conformance/lexicon.md</c>). The corpus under <c>conformance/formats/lexicon/</c> holds the reader to it: each
/// valid lexicon elaborates to its glossary byte for byte, every valid glossary has a lexicon, and each invalid one is
/// refused at the line its first comment names.
/// </summary>
public partial class LexiconTests
{
    private static string Formats => Path.Combine(FixturePaths.ConformanceRoot, "formats");

    private static string Valid => Path.Combine(Formats, "lexicon", "valid");

    private static string Invalid => Path.Combine(Formats, "lexicon", "invalid");

    private static string GlossaryOf(string lexicon)
    {
        var name = Path.GetFileNameWithoutExtension(lexicon);
        var own = Path.Combine(Valid, name + ".json");
        return File.Exists(own) ? own : Path.Combine(Formats, "glossary", "valid", name + ".json");
    }

    public static IEnumerable<string> ValidLexicons() =>
        Directory
            .GetFiles(Valid, "*.lexicon")
            .Order(StringComparer.Ordinal)
            .Select(Path.GetFileName)!;

    public static IEnumerable<string> InvalidLexicons() =>
        Directory
            .GetFiles(Invalid, "*.lexicon")
            .Order(StringComparer.Ordinal)
            .Select(Path.GetFileName)
            .Concat(Directory.GetDirectories(Invalid).Select(Path.GetFileName))!;

    public static IEnumerable<string> ValidGlossaries() =>
        Directory
            .GetFiles(Path.Combine(Formats, "glossary", "valid"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(Path.GetFileName)!;

    [TestCaseSource(nameof(ValidLexicons))]
    public void A_valid_lexicon_elaborates_to_its_glossary_byte_for_byte(string name)
    {
        var path = Path.Combine(Valid, name);
        var expected = File.ReadAllText(GlossaryOf(path));

        var reading = Lexicon.Read(path, File.ReadAllText(path));

        reading.Problems.Should().BeEmpty();
        reading.Json.Should().Be(expected);
        // Backed by meaning, not only bytes: the two read to the same document.
        JsonNode
            .DeepEquals(JsonNode.Parse(reading.Json!), JsonNode.Parse(expected))
            .Should()
            .BeTrue();
        Glossary.Read(expected).Machines.Keys.Should().Equal(reading.Glossary!.Inner.Machines.Keys);
    }

    [TestCaseSource(nameof(ValidGlossaries))]
    public void Every_valid_glossary_has_a_lexicon(string name)
    {
        File.Exists(Path.Combine(Valid, Path.ChangeExtension(name, Lexicon.Extension)))
            .Should()
            .BeTrue($"formats/glossary/valid/{name} is a glossary a lexicon must be able to say");
    }

    [TestCaseSource(nameof(ValidGlossaries))]
    public void Every_valid_glossary_is_written_in_the_canonical_form(string name)
    {
        var text = File.ReadAllText(Path.Combine(Formats, "glossary", "valid", name));

        GlossaryWriter.Write(JsonNode.Parse(text)!.AsObject()).Should().Be(text);
    }

    [TestCaseSource(nameof(InvalidLexicons))]
    public void An_invalid_lexicon_is_refused_at_its_line(string name)
    {
        var path = Path.Combine(Invalid, name);
        var directory = Directory.Exists(path);
        var first = directory
            ? Directory.GetFiles(path, "*.lexicon").Order(StringComparer.Ordinal).First()
            : path;
        var expect = Expect().Match(File.ReadLines(first).First());
        expect
            .Success.Should()
            .BeTrue($"{name} starts with '# expect <line>: <words of the problem>'");
        var line = long.Parse(
            expect.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture
        );
        var words = expect.Groups[2].Value.Trim();

        var reading = directory
            ? Lexicon.ReadDirectory(path)!
            : Lexicon.Read(path, File.ReadAllText(path));

        reading.Succeeded.Should().BeFalse();
        reading.Json.Should().BeNull();
        reading.Problems.Should().NotBeEmpty();
        var problem = reading.Problems[0];
        problem.Message.Should().Contain(words, $"the problem was {problem}");
        problem.Line.Should().Be(line, $"the problem was {problem}");
        problem.Message.Should().NotContain("glossary.json");
        problem.Message.Should().NotContain("/machines/");
    }

    [Test]
    public void A_problem_derivation_finds_with_the_glossary_is_reported_at_its_lexicon_line()
    {
        const string lexicon = """
            Version: 1
            Actor: member "a member"
              Any signed-in
            Machine: note
              Owned by: member
              Started by: "has a note"
              Differential
              State: Open "is open"
            """;
        const string feature = """
            Feature: Notes
              Scenario: A member has a note
                Given Ada is a member
                And Ada has a note
                Then Ada's note is open
            """;

        var reading = Lexicon.Read("notes.lexicon", lexicon);
        reading.Problems.Should().BeEmpty();
        var derived = FeatureMachines.Derive(
            reading.Glossary!,
            [new FeatureFile("notes.feature", feature)]
        );

        derived.Problems.Should().ContainSingle();
        derived.Problems[0].File.Should().Be("notes.lexicon");
        derived.Problems[0].Line.Should().Be(7);
        derived.Problems[0].Message.Should().Contain("asks for a differential block");
    }

    [Test]
    public void A_lexicon_elaborates_the_same_with_any_indentation_and_tabs_between_columns()
    {
        var path = Path.Combine(Valid, "battleship.lexicon");
        var text = File.ReadAllText(path);
        var flat = string.Join(
            "\n",
            text.Split('\n').Select(l => l.TrimStart()).Select(l => Columns().Replace(l, "\t"))
        );

        Lexicon.Read(path, flat).Json.Should().Be(Lexicon.Read(path, text).Json).And.NotBeNull();
    }

    // A run of spaces outside quotes; the fixtures hold no quote with two spaces in it.
    [GeneratedRegex(@"  +(?=(?:[^""]*""[^""]*"")*[^""]*\z)")]
    private static partial Regex Columns();

    [GeneratedRegex(@"\A#\s*expect\s+(\d+):(.*)\z")]
    private static partial Regex Expect();
}
