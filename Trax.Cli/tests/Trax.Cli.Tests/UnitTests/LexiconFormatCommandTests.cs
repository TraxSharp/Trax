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

    private string _dir = null!;
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

    private static (int Exit, string Out, string Err) Format(string path, bool check = false)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = LexiconCommand.RunFormat([path], check, stdout, stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    [Test]
    public void Formatting_twice_changes_nothing_the_second_time_and_the_glossary_stays_the_same()
    {
        var before = Lexicon.Read(File_, Untidy).Json;

        Format(_dir).Exit.Should().Be(0);
        var once = File.ReadAllText(File_);
        var again = Format(_dir);

        again.Exit.Should().Be(0);
        again.Out.Should().BeEmpty("a formatted lexicon is already in its layout");
        File.ReadAllText(File_).Should().Be(once);
        Lexicon.Read(File_, once).Json.Should().NotBeNull().And.Be(before);
    }

    [Test]
    public void A_lexicon_is_indented_by_block_and_its_columns_lined_up()
    {
        Format(File_);

        File.ReadAllText(File_)
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

                """.Replace("\r\n", "\n")
            );
    }

    [Test]
    public void Check_fails_on_a_lexicon_not_in_its_layout_and_writes_nothing()
    {
        var (exit, _, err) = Format(File_, check: true);

        exit.Should().Be(1);
        err.Should().Contain("is not in its layout");
        File.ReadAllText(File_).Should().Be(Untidy);
    }

    [Test]
    public void A_line_it_cannot_read_is_reported_at_its_line_and_the_file_is_left_alone()
    {
        File.WriteAllText(File_, Untidy.Replace("Roles:", "Rolez:"));

        var (exit, _, err) = Format(File_);

        exit.Should().Be(1);
        err.Should().Contain("shop.lexicon:4:").And.Contain("did you mean 'Roles:'");
    }
}
