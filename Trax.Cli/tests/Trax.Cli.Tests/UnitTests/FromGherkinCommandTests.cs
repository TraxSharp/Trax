using AwesomeAssertions;
using Trax.Cli.Commands;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine from-gherkin</c> owns the directory it writes (ADR <c>Trax.Docs/adr/0058</c>): the IR of a machine
/// that stops deriving is removed, <c>--check</c> fails on it and on a stale <c>access.txt</c> first, a directory holding
/// anything else is refused untouched, and every bad path is a message and exit 1, never an exception.
/// </summary>
public class FromGherkinCommandTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": {
            "customer": { "noun": "a customer", "roles": ["customer"] },
            "baker": { "noun": "a baker", "roles": ["baker"] }
          },
          "machines": {
            "order": {
              "ownedBy": "customer",
              "noun": "order",
              "start": "has an order",
              "initial": "Draft",
              "states": { "Draft": "is a draft", "Placed": "is placed" },
              "triggers": { "Place": { "phrase": "places {subject}" } }
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

    private const string Ordering = """
        Feature: Ordering
          Scenario: Ada places her order
            Given Ada is a customer
            And Ada has an order
            When Ada places her order
            Then Ada's order is placed
        """;

    private const string Opening = """
        Feature: Opening
          Scenario: The shop opens
            Given Kit is a baker
            And the shop is closed
            When the shop opens
            Then the shop is open
        """;

    private string _dir = null!;
    private string Features => Path.Combine(_dir, "features");
    private string Out => Path.Combine(_dir, "machines");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-from-gherkin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Features);
        File.WriteAllText(Path.Combine(Features, "glossary.json"), Glossary);
        File.WriteAllText(Path.Combine(Features, "ordering.feature"), Ordering);
        File.WriteAllText(Path.Combine(Features, "opening.feature"), Opening);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void It_writes_one_ir_per_machine_the_host_file_and_the_access_report()
    {
        var (exit, _, err) = Run();

        exit.Should().Be(0, err);
        Directory
            .EnumerateFiles(Out)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo(["order.ir.json", "shop.ir.json", "host.json", "access.txt"]);
        File.ReadAllText(Path.Combine(Out, "access.txt")).Should().Contain("Place");
        Run(check: true).Exit.Should().Be(0, "nothing changed since it was written");
    }

    [Test]
    public void A_machine_that_stops_deriving_fails_the_check_then_is_removed()
    {
        Run();
        File.Delete(Path.Combine(Features, "opening.feature"));

        var check = Run(check: true);
        check.Exit.Should().Be(1);
        check.Err.Should().Contain("shop.ir.json no longer derives");

        Run().Exit.Should().Be(0);
        File.Exists(Path.Combine(Out, "shop.ir.json")).Should().BeFalse();
        Run(check: true).Exit.Should().Be(0);
    }

    [Test]
    public void A_changed_grant_makes_the_access_report_stale()
    {
        Run();
        var glossary = Path.Combine(Features, "glossary.json");
        File.WriteAllText(
            glossary,
            File.ReadAllText(glossary)
                .Replace("\"roles\": [\"customer\"]", "\"roles\": [\"shopper\"]")
        );

        var check = Run(check: true);

        check.Exit.Should().Be(1);
        check.Err.Should().Contain("access.txt is stale");
    }

    [Test]
    public void An_out_that_is_a_file_is_refused()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "order.ir.json");
        File.WriteAllText(file, "{}");

        var (exit, _, err) = Run(outDir: file);

        exit.Should().Be(1);
        err.Should().Contain("--out is a file");
        File.ReadAllText(file).Should().Be("{}");
    }

    [Test]
    public void An_out_holding_files_it_did_not_write_is_refused_untouched()
    {
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "notes.md"), "mine");

        var (exit, _, err) = Run();

        exit.Should().Be(1);
        err.Should().Contain("notes.md");
        Directory.EnumerateFiles(Out).Select(Path.GetFileName).Should().Equal("notes.md");
    }

    [Test]
    public void A_missing_glossary_is_a_message()
    {
        File.Delete(Path.Combine(Features, "glossary.json"));

        var (exit, _, err) = Run();

        exit.Should().Be(1);
        err.Should().Contain("There is no glossary at");
    }

    [Test]
    public void A_bad_glossary_names_its_key()
    {
        File.WriteAllText(Path.Combine(Features, "glossary.json"), """{ "version": 2 }""");

        var (exit, _, err) = Run();

        exit.Should().Be(1);
        err.Should().Contain("glossary.json").And.Contain("version");
    }

    [Test]
    public void A_problem_writes_nothing()
    {
        File.AppendAllText(
            Path.Combine(Features, "ordering.feature"),
            """

              Scenario: Nonsense
                Given Ada is a customer
                When Ada juggles her order
                Then Ada's order is placed
            """
        );

        var (exit, _, err) = Run();

        exit.Should().Be(1);
        err.Should().Contain("ordering.feature:");
        Directory.Exists(Out).Should().BeFalse();
    }

    [Test]
    public void Feature_paths_are_relative_so_the_output_does_not_depend_on_where_it_runs()
    {
        Run();

        File.ReadAllText(Path.Combine(Out, "host.json"))
            .Should()
            .Contain("\"file\":\"ordering.feature\"")
            .And.NotContain(_dir);
    }

    private (int Exit, string Out, string Err) Run(bool check = false, string? outDir = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = MachineCommand.RunFromGherkin(
            Features,
            Path.Combine(Features, "glossary.json"),
            outDir ?? Out,
            check,
            stdout,
            stderr
        );
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
