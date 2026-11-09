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

    [Test]
    public void Run_passes_the_features_on_the_committed_machines()
    {
        Run();

        var (exit, stdout, err) = RunFeatures();

        exit.Should().Be(0, err);
        stdout.Should().Contain("ordering.feature:2 Ada places her order  passed");
        stdout.Should().Contain("2 scenario(s): 2 passed");
    }

    [Test]
    public void Run_fails_a_scenario_the_committed_machines_answer_differently()
    {
        Run();
        File.AppendAllText(
            Path.Combine(Features, "ordering.feature"),
            """

              Scenario: Placing twice
                Given Ada is a customer
                And Ada's order is placed
                When Ada places her order
                Then Ada's order is placed
            """
        );

        var (exit, stdout, _) = RunFeatures();

        exit.Should().Be(1);
        stdout.Should().Contain("Placing twice  failed").And.Contain("NO_TRANSITION");
    }

    [Test]
    public void Run_selects_by_location_and_writes_junit()
    {
        Run();
        var report = Path.Combine(_dir, "report.xml");

        var (exit, _, err) = RunFeatures(
            new MachineCommand.RunArguments(
                Features,
                Path.Combine(Features, "glossary.json"),
                Out,
                ["opening.feature:2"],
                Format: "junit",
                ReportPath: report
            )
        );

        exit.Should().Be(0, err);
        var xml = File.ReadAllText(report);
        xml.Should().Contain("The shop opens (line 2)").And.NotContain("Ada places her order");
    }

    [Test]
    public void Run_writes_junit_for_names_and_steps_xml_cannot_hold()
    {
        Run();
        File.WriteAllText(
            Path.Combine(Features, "odd.feature"),
            "Feature: Odd\n"
                + "  Scenario: The shop\u000Bopens\n"
                + "    Given Kit is a baker\n"
                + "    And the shop is closed\n"
                + "    When the shop opens\n"
                + "    Then the shop\u000Bis closed\n"
        );
        var report = Path.Combine(_dir, "report.xml");

        var (exit, _, err) = RunFeatures(
            new MachineCommand.RunArguments(
                Features,
                Path.Combine(Features, "glossary.json"),
                Out,
                ["odd.feature:2"],
                Format: "junit",
                ReportPath: report
            )
        );

        err.Should().NotContain("Exception");
        File.Exists(report).Should().BeTrue(err);
        exit.Should().Be(1, "the shop is open, not closed");
        var xml = File.ReadAllText(report);
        System.Xml.Linq.XDocument.Parse(xml);
        xml.Should().Contain("The shop?opens (line 2)").And.Contain("the shop?is closed");
    }

    [Test]
    public void The_junit_report_keeps_surrogate_pairs_and_replaces_lone_surrogates() =>
        Trax
            .Cli.Machines.RunReportWriter.Xml("a\uD83D\uDE00b\uD800c\uDE00\uFFFE")
            .Should()
            .Be("a\uD83D\uDE00b?c??");

    [Test]
    public void Run_refuses_a_machines_directory_whose_host_file_was_derived_from_other_irs()
    {
        Run();
        var ir = Path.Combine(Out, "order.ir.json");
        File.WriteAllText(ir, File.ReadAllText(ir).Replace("\"Placed\"", "\"Sent\""));

        var (exit, _, err) = RunFeatures();

        exit.Should().Be(1);
        err.Should().Contain("order");
    }

    [Test]
    public void Run_refuses_a_bad_tag_expression_and_a_bad_location()
    {
        Run();

        RunFeatures(
            new MachineCommand.RunArguments(
                Features,
                Path.Combine(Features, "glossary.json"),
                Out,
                [],
                Tags: "@a and"
            )
        )
            .Err.Should()
            .Contain("--tags");
        RunFeatures(
            new MachineCommand.RunArguments(
                Features,
                Path.Combine(Features, "glossary.json"),
                Out,
                ["ordering"]
            )
        )
            .Err.Should()
            .Contain("is not file:line");
    }

    [Test]
    public void Run_fails_when_no_scenario_matches()
    {
        Run();
        var glossary = Path.Combine(Features, "glossary.json");

        var tag = RunFeatures(
            new MachineCommand.RunArguments(Features, glossary, Out, [], Tags: "@smok")
        );
        var location = RunFeatures(
            new MachineCommand.RunArguments(Features, glossary, Out, ["ordering.feature:99"])
        );

        tag.Exit.Should().Be(1, "a selection that runs nothing is not a pass");
        tag.Err.Should().Contain("No scenario matched");
        location.Exit.Should().Be(1, "a location that names no scenario is not a pass");
        location.Err.Should().Contain("No scenario matched");
    }

    [Test]
    public void Run_fails_a_selection_whose_every_scenario_is_host_only()
    {
        File.WriteAllText(
            Path.Combine(Features, "hosted.feature"),
            """
            Feature: Hosted
              @host
              Scenario: Ada places her order on the host
                Given Ada is a customer
                And Ada has an order
                When Ada places her order
                Then Ada's order is placed
            """
        );
        Run();
        var glossary = Path.Combine(Features, "glossary.json");

        var hostOnly = RunFeatures(
            new MachineCommand.RunArguments(Features, glossary, Out, [], Tags: "@host")
        );
        var all = RunFeatures();

        hostOnly.Exit.Should().Be(1, "a selection that runs nothing is not a pass");
        hostOnly.Err.Should().Contain("tagged @host").And.Contain("nothing ran");
        all.Exit.Should().Be(0, all.Err + all.Out);
    }

    [Test]
    public void Run_refuses_a_name_that_takes_too_long_to_match()
    {
        File.WriteAllText(
            Path.Combine(Features, "opening.feature"),
            Opening.Replace("Scenario: The shop opens", "Scenario: " + new string('a', 40) + "!")
        );
        Run();

        var (exit, _, err) = RunFeatures(
            new MachineCommand.RunArguments(
                Features,
                Path.Combine(Features, "glossary.json"),
                Out,
                [],
                Names: ["(a+)+$"]
            )
        );

        exit.Should().Be(1);
        err.Should().Contain("--name '(a+)+$'");
    }

    [Test]
    public void Check_and_write_read_a_crlf_checkout_as_unchanged()
    {
        Run();
        foreach (var path in Directory.EnumerateFiles(Out))
            File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n"));

        var check = Run(check: true);
        var write = Run();

        check.Exit.Should().Be(0, check.Err);
        write.Exit.Should().Be(0, write.Err);
        write.Out.Should().NotContain("updated", "a CRLF checkout is not a change to write");
    }

    [Test]
    public void Missing_lists_each_gap_with_a_stub_and_writes_them_to_paste()
    {
        File.WriteAllText(
            Path.Combine(Features, "glossary.json"),
            Glossary.Replace(
                "\"Place\": { \"phrase\": \"places {subject}\" }",
                "\"Place\": { \"phrase\": \"places {subject}\" }, \"Cancel\": { \"phrase\": \"cancels {subject}\" }"
            )
        );
        var stubs = Path.Combine(_dir, "missing.feature");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = MachineCommand.RunMissing(
            Features,
            Path.Combine(Features, "glossary.json"),
            ["trigger"],
            stubs,
            stdout,
            stderr
        );

        exit.Should().Be(0, stderr.ToString());
        stdout
            .ToString()
            .Should()
            .Contain("# order Cancel: no scenario fires it")
            .And.Contain("1 gap(s).");
        File.ReadAllText(stubs)
            .Should()
            .StartWith("Feature: Missing scenarios")
            .And.Contain("When Ada cancels their order");
    }

    [Test]
    public void Usage_lists_each_phrase_with_the_steps_that_say_it()
    {
        var stdout = new StringWriter();

        var exit = MachineCommand.RunUsage(
            Features,
            Path.Combine(Features, "glossary.json"),
            false,
            stdout,
            new StringWriter()
        );

        exit.Should().Be(0);
        stdout
            .ToString()
            .Should()
            .Contain("order trigger Place \"places {subject}\": 1\n  ordering.feature:5");
    }

    [Test]
    public void Usage_reports_a_feature_it_cannot_read()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("file modes are Unix-only");
            return;
        }
        var feature = Path.Combine(Features, "ordering.feature");
        File.SetUnixFileMode(feature, UnixFileMode.None);
        var stderr = new StringWriter();
        try
        {
            var exit = MachineCommand.RunUsage(
                Features,
                Path.Combine(Features, "glossary.json"),
                false,
                new StringWriter(),
                stderr
            );

            exit.Should().Be(1);
            stderr.ToString().Should().Contain("ordering.feature");
        }
        finally
        {
            File.SetUnixFileMode(feature, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [TestCase("json")]
    [TestCase("mermaid")]
    [TestCase("html")]
    public void Diagram_draws_the_board_with_the_scenarios_behind_each_sticky(string format)
    {
        Run();
        var stdout = new StringWriter();

        var exit = MachineCommand.RunDiagram(Out, format, null, stdout, new StringWriter());

        exit.Should().Be(0);
        stdout.ToString().Should().Contain("Place");
        if (format != "html")
            stdout.ToString().Should().Contain("Draft → Placed");
        if (format != "mermaid")
            stdout.ToString().Should().Contain("Ada places her order");
    }

    [Test]
    public void The_html_board_never_parses_a_label_as_markup()
    {
        File.WriteAllText(
            Path.Combine(Features, "ordering.feature"),
            Ordering.Replace(
                "Scenario: Ada places her order",
                "Scenario: Ada places </script><b>x</b>"
            )
        );
        Run();
        var stdout = new StringWriter();

        MachineCommand.RunDiagram(Out, "html", null, stdout, new StringWriter()).Should().Be(0);

        var html = stdout.ToString();
        html.Should().NotContain("</script><b>").And.NotContain("innerHTML");
        html.Should().Contain("\\u003C/script\\u003E");
    }

    [Test]
    public void The_mermaid_board_quotes_each_machine_and_keeps_each_label_on_its_line()
    {
        File.WriteAllText(
            Path.Combine(Features, "glossary.json"),
            Glossary
                .Replace("\"shop\": {", "\"end\": {")
                .Replace("shop/main", "end/main")
                .Replace("\"order\": {", "\"n0\": {")
                .Replace("\"roles\": [\"customer\"]", "\"roles\": [\"cus\\ntomer#1\"]")
        );
        var derived = Run();
        derived.Exit.Should().Be(0, derived.Err);
        var stdout = new StringWriter();

        MachineCommand.RunDiagram(Out, "mermaid", null, stdout, new StringWriter()).Should().Be(0);

        var lines = stdout.ToString().Split('\n');
        lines
            .Where(l => l.TrimStart().StartsWith("subgraph", StringComparison.Ordinal))
            .Should()
            .Equal("  subgraph m0[\"end\"]", "  subgraph m1[\"n0\"]");
        lines.Should().Contain(l => l.Contains("[\"cus tomer#35;1\"]"));
        lines
            .Where(l => l.Length > 0)
            .Should()
            .OnlyContain(
                l => l == "flowchart LR" || l.StartsWith("  ", StringComparison.Ordinal),
                "no label breaks its line"
            );
    }

    [Test]
    public void Diagram_refuses_a_directory_with_no_host_file()
    {
        var stderr = new StringWriter();

        MachineCommand
            .RunDiagram(Features, "json", null, new StringWriter(), stderr)
            .Should()
            .Be(1);
        stderr.ToString().Should().Contain("has no host.json");
    }

    // A quiz whose Between broadcast clears the sheets its Scoring broadcast closes: derivation warns, and writes.
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

          Scenario: The answers are scored and the closed sheets are cleared
            Given Ada's answer sheet is closed
            And the quiz is scoring the question
            When the answers are scored
            Then the quiz is between questions
            And Ada's answer sheet is waiting for a question
        """;

    [Test]
    public void A_warning_is_printed_to_stderr_and_fails_neither_the_write_nor_the_check()
    {
        File.WriteAllText(Path.Combine(Features, "glossary.json"), Quiz);
        File.Delete(Path.Combine(Features, "ordering.feature"));
        File.Delete(Path.Combine(Features, "opening.feature"));
        File.WriteAllText(Path.Combine(Features, "quiz.feature"), Sheets);

        var (exit, _, err) = Run();
        var (checkExit, checkOut, checkErr) = Run(check: true);

        exit.Should().Be(0, err);
        err.Should()
            .StartWith(
                "warning: quiz.feature:52: When quiz enters Between, the policy quiz.Between>answer-sheet.Clear moves answer-sheet instances in Closed;"
            );
        File.Exists(Path.Combine(Out, "host.json")).Should().BeTrue();
        checkExit.Should().Be(0, checkErr);
        checkErr.Should().Contain("warning: quiz.feature:52:");
        checkOut.Should().Contain("is up to date");
    }

    private (int Exit, string Out, string Err) RunFeatures(MachineCommand.RunArguments? args = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = MachineCommand.RunRun(
            args
                ?? new MachineCommand.RunArguments(
                    Features,
                    Path.Combine(Features, "glossary.json"),
                    Out,
                    []
                ),
            stdout,
            stderr
        );
        return (exit, stdout.ToString(), stderr.ToString());
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
