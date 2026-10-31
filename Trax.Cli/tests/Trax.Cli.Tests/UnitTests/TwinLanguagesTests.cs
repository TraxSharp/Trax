using System.CommandLine;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Machines;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The languages <c>trax machine generate --lang</c> takes come from the engine's <c>twin-languages.json</c>, the
/// file generate-twin.mjs reads, so a language added there is one the CLI validates and lists without an edit here.
/// </summary>
public class TwinLanguagesTests
{
    [Test]
    public void All_is_the_file_the_twin_generator_reads()
    {
        var file = Path.Combine(EngineTools(), "twin-languages.json");
        var expected = JsonNode
            .Parse(File.ReadAllText(file))!
            .AsArray()
            .Select(l => new TwinLanguage(
                l!["lang"]!.GetValue<string>(),
                l["writes"]!.GetValue<string>()
            ));

        TwinLanguages.All.Should().Equal(expected);
        TwinLanguages.Default.Should().Be("typescript");
    }

    [Test]
    public void OneOf_lists_the_names_as_the_twin_generator_does()
    {
        TwinLanguages.OneOf(["typescript", "csharp"]).Should().Be("'typescript' or 'csharp'");
        TwinLanguages
            .OneOf(["typescript", "csharp", "swift"])
            .Should()
            .Be("'typescript', 'csharp' or 'swift'");
        TwinLanguages.OneOf(["typescript"]).Should().Be("'typescript'");
    }

    [Test]
    public void Help_reads_as_it_did_when_the_languages_were_written_into_it()
    {
        TwinLanguages
            .Help([
                new("typescript", "<id>.contexts.g.ts and <id>.machine.g.ts"),
                new("csharp", "<Machine>.g.cs, for Trax.StateMachine.Client"),
            ])
            .Should()
            .Be(
                "The typed view --twin-out writes: 'typescript' (default; <id>.contexts.g.ts and "
                    + "<id>.machine.g.ts) or 'csharp' (<Machine>.g.cs, for Trax.StateMachine.Client)."
            );
    }

    [TestCase("generate")]
    [TestCase("check")]
    public void The_lang_option_describes_every_language(string subcommand)
    {
        var lang = MachineCommand
            .Create()
            .Subcommands.Single(c => c.Name == subcommand)
            .Options.Single(o => o.Name == "--lang");

        lang.Description.Should().Be(TwinLanguages.Help());
    }

    private static string EngineTools()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = Path.Combine(dir.FullName, "Trax.StateMachine", "ts", "tools");
            if (File.Exists(Path.Combine(candidate, "generate-twin.mjs")))
                return candidate;
        }
        throw new AssertionException("No Trax.StateMachine/ts/tools above the test binary.");
    }
}
