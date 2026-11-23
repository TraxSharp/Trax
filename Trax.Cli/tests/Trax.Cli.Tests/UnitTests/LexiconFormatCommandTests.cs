using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax lexicon format</c> lays a lexicon out in one way, and since the reader ignores layout, what the lexicon
/// elaborates to is the same before and after (ADR <c>Trax.Docs/adr/0059</c>).
/// </summary>
public class LexiconFormatCommandTests
{
    private const string Untidy = """
        Version: 1
        # Who uses it.
        Actor:   customer "a customer"
        Roles: customer
        Machine: order
        Owned by: customer
        Started by:  "has an order"


        State: Draft "is a draft"
        State: PaymentTaken "is paid for"   # the train ran
        Trigger: Place "places {subject}"
        Runs: Shop.Trains.ITakePayment in PaymentTaken
        Returns: approved is a boolean
        Outcome: "the payment goes through" when approved is true
        Outcome: "it fails" when it fails
        """;

    private string _dir = "";
    private string File_ => Path.Combine(_dir, "shop.lexicon");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-lexicon-format-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, Untidy);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private static async Task<(int Exit, string Out, string Err)> FormatAsync(
        string path,
        bool check = false
    )
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await LexiconCommand
            .RunFormatAsync([path], check, stdout, stderr)
            .ConfigureAwait(false);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Test]
    public async Task Formatting_twice_changes_nothing_the_second_time_and_the_glossary_stays_the_same()
    {
        var before = Lexicon.Read(File_, Untidy).Json;

        (await FormatAsync(_dir).ConfigureAwait(false)).Exit.Should().Be(0);
        var once = await File.ReadAllTextAsync(File_).ConfigureAwait(false);
        var again = await FormatAsync(_dir).ConfigureAwait(false);

        again.Exit.Should().Be(0);
        again.Out.Should().BeEmpty("a formatted lexicon is already in its layout");
        (await File.ReadAllTextAsync(File_).ConfigureAwait(false)).Should().Be(once);
        Lexicon.Read(File_, once).Json.Should().NotBeNull().And.Be(before);
    }

    [Test]
    public async Task A_lexicon_is_indented_by_block_and_its_columns_lined_up()
    {
        await FormatAsync(File_).ConfigureAwait(false);
        (await File.ReadAllTextAsync(File_).ConfigureAwait(false))
            .Should()
            .Be(
                """
                Version: 1
                # Who uses it.
                Actor: customer "a customer"
                  Roles: customer
                Machine: order
                  Owned by: customer
                  Started by: "has an order"

                  State: Draft        "is a draft"
                  State: PaymentTaken "is paid for"  # the train ran
                  Trigger: Place "places {subject}"
                  Runs: Shop.Trains.ITakePayment in PaymentTaken
                    Returns: approved is a boolean
                    Outcome: "the payment goes through" when approved is true
                    Outcome: "it fails"                 when it fails

                """.Replace("\r\n", "\n", StringComparison.Ordinal)
            );
    }

    [Test]
    public async Task Check_fails_on_a_lexicon_not_in_its_layout_and_writes_nothing()
    {
        var (exit, _, err) = await FormatAsync(File_, check: true).ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("is not in its layout");
        (await File.ReadAllTextAsync(File_).ConfigureAwait(false)).Should().Be(Untidy);
    }

    [Test]
    public async Task Build_output_and_dependencies_are_not_formatted()
    {
        await FormatAsync(_dir).ConfigureAwait(false);
        foreach (var copy in new[] { "bin", "obj", "node_modules", ".git" })
        {
            Directory.CreateDirectory(Path.Combine(_dir, copy));
            await File.WriteAllTextAsync(Path.Combine(_dir, copy, "shop.lexicon"), Untidy)
                .ConfigureAwait(false);
        }

        var (exit, _, err) = await FormatAsync(_dir, check: true).ConfigureAwait(false);

        exit.Should().Be(0, err);
        (
            await File.ReadAllTextAsync(Path.Combine(_dir, "bin", "shop.lexicon"))
                .ConfigureAwait(false)
        )
            .Should()
            .Be(Untidy);
    }

    [Test]
    public async Task A_line_it_cannot_read_is_reported_at_its_line_and_the_file_is_left_alone()
    {
        await File.WriteAllTextAsync(
            File_,
            Untidy.Replace("Roles:", "Rolez:", StringComparison.Ordinal)
        );

        var (exit, _, err) = await FormatAsync(File_).ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("shop.lexicon:4:").And.Contain("did you mean 'Roles:'");
    }
}
