using AwesomeAssertions;
using Trax.Cli.Commands;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine from-gherkin</c> owns the directory it writes (ADR <c>Trax.Docs/adr/0058</c>, and for the glossary it
/// elaborates from the lexicon <c>Trax.Docs/adr/0059</c>): the IR of a machine
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

    // The glossary above, as a lexicon.
    private const string Lexicon = """
        Version: 1

        Actor: customer "a customer"
          Roles: customer
        Actor: baker "a baker"
          Roles: baker

        Machine: order
          Owned by: customer
          Started by: "has an order"
          State: Draft  "is a draft"
          State: Placed "is placed"
          Trigger: Place "places {subject}"

        Machine: shop "the shop"
          System
          State: Closed "is closed"
          State: Open   "is open"
          Trigger: Open "the shop opens"
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

    private string _dir = "";
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
    public async Task A_glossary_json_with_no_lexicon_is_refused_naming_the_way_to_read_it()
    {
        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("holds glossary.json and no lexicon").And.Contain("--glossary");
        Directory.Exists(Out).Should().BeFalse();
    }

    [Test]
    public async Task A_glossary_inside_out_is_refused_and_left_where_it_is()
    {
        Directory.CreateDirectory(Out);
        var inside = Path.Combine(Out, "glossary.json");
        File.Move(Path.Combine(Features, "glossary.json"), inside);

        var (exit, _, err) = await RunAsync(glossary: inside).ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("is inside --out");
        (await File.ReadAllTextAsync(inside).ConfigureAwait(false)).Should().Be(Glossary);
    }

    [Test]
    public async Task Trains_with_a_json_glossary_is_refused()
    {
        var (exit, _, err) = await RunAsync(
                trains: [typeof(LexiconTrains.ICheckRefundTrain).Assembly.Location]
            )
            .ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("--trains is read with a lexicon");
    }

    [Test]
    public async Task Trains_that_are_not_there_is_a_message()
    {
        await UseLexiconAsync().ConfigureAwait(false);

        var stderr = new StringWriter();
        var exit = await MachineCommand
            .RunFromGherkinAsync(
                Features,
                glossaryPath: null,
                Out,
                check: false,
                new StringWriter(),
                stderr,
                [Path.Combine(_dir, "Missing.dll")]
            )
            .ConfigureAwait(false);

        exit.Should().Be(1);
        stderr.ToString().Should().Contain("--trains: Assembly not found");
    }

    [Test]
    public async Task Trains_give_a_lexicon_the_fields_its_Runs_lines_leave_out()
    {
        await UseLexiconAsync(
                Lexicon
                    + $"""

                    Machine: refund
                      Owned by: customer
                      Context: reason is a string
                      State: Asked "is asked for"
                      State: Checking "is being checked"
                      Runs: {typeof(LexiconTrains.ICheckRefundTrain).FullName} in Checking
                        Stores: approved
                        Outcome: "the refund is approved" when approved is true
                        Outcome: "the refund cannot be checked" when it fails
                    """
            )
            .ConfigureAwait(false);
        var without = new StringWriter();
        var withTrains = new StringWriter();

        var refused = await MachineCommand
            .RunUsageAsync(
                Features,
                glossaryPath: null,
                unusedOnly: false,
                new StringWriter(),
                without
            )
            .ConfigureAwait(false);
        var read = await MachineCommand
            .RunUsageAsync(
                Features,
                glossaryPath: null,
                unusedOnly: false,
                new StringWriter(),
                withTrains,
                [typeof(LexiconTrains.ICheckRefundTrain).Assembly.Location]
            )
            .ConfigureAwait(false);

        refused.Should().Be(1);
        without.ToString().Should().Contain("returns no 'approved'");
        read.Should().Be(0, withTrains.ToString());
    }

    [Test]
    public async Task A_lexicon_is_elaborated_and_its_glossary_written_with_the_machines()
    {
        await UseLexiconAsync().ConfigureAwait(false);

        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(0, err);
        Directory
            .EnumerateFiles(Out)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo([
                "order.ir.json",
                "shop.ir.json",
                "host.json",
                "access.txt",
                "glossary.json",
            ]);
        (await File.ReadAllTextAsync(Path.Combine(Out, "glossary.json")).ConfigureAwait(false))
            .Should()
            .Be(
                Trax.StateMachine.Gherkin.Lexicon.Read("shop.lexicon", Lexicon).Json,
                "the glossary written is the lexicon's, in its canonical form"
            );
        (await FromLexiconAsync(check: true).ConfigureAwait(false))
            .Exit.Should()
            .Be(0, "nothing changed since it was written");
        (await FromLexiconAsync().ConfigureAwait(false))
            .Out.Should()
            .Contain("is up to date", "a glossary it wrote is not a foreign file");
    }

    [Test]
    public async Task A_changed_lexicon_makes_the_glossary_stale()
    {
        await UseLexiconAsync().ConfigureAwait(false);
        await FromLexiconAsync().ConfigureAwait(false);
        await UseLexiconAsync(
                Lexicon.Replace("\"is a draft\"", "\"is still a draft\"", StringComparison.Ordinal)
            )
            .ConfigureAwait(false);

        var check = await FromLexiconAsync(check: true).ConfigureAwait(false);

        check.Exit.Should().Be(1);
        check.Err.Should().Contain("glossary.json is stale");
    }

    [Test]
    public async Task A_lexicon_beside_a_glossary_json_is_refused()
    {
        await File.WriteAllTextAsync(Path.Combine(Features, "shop.lexicon"), Lexicon);

        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("holds a lexicon and glossary.json");
        Directory.Exists(Out).Should().BeFalse();
    }

    [Test]
    public async Task Two_lexicons_in_one_directory_are_refused()
    {
        await UseLexiconAsync().ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(Features, "more.lexicon"), Lexicon)
            .ConfigureAwait(false);

        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("this directory's lexicon already");
    }

    [Test]
    public async Task A_lexicon_problem_names_its_line_and_writes_nothing()
    {
        await UseLexiconAsync(
                Lexicon.Replace(
                    "Trigger: Place",
                    "Trigger: Place\n    Stores: x",
                    StringComparison.Ordinal
                )
            )
            .ConfigureAwait(false);

        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("shop.lexicon:14: 'Stores:' belongs under a 'Runs:' block");
        err.Should().NotContain("glossary.json");
        Directory.Exists(Out).Should().BeFalse();
    }

    [Test]
    public async Task A_directory_with_neither_a_lexicon_nor_a_glossary_is_a_message()
    {
        File.Delete(Path.Combine(Features, "glossary.json"));

        var (exit, _, err) = await FromLexiconAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("holds no lexicon");
    }

    [Test]
    public async Task Run_missing_and_usage_read_the_lexicon_each_time()
    {
        await UseLexiconAsync().ConfigureAwait(false);
        await FromLexiconAsync().ConfigureAwait(false);
        var usage = new StringWriter();

        (
            await RunFeaturesAsync(
                    new MachineCommand.RunArguments(Features, GlossaryPath: null, Out, [])
                )
                .ConfigureAwait(false)
        )
            .Exit.Should()
            .Be(0);
        (
            await MachineCommand
                .RunUsageAsync(
                    Features,
                    glossaryPath: null,
                    unusedOnly: false,
                    usage,
                    new StringWriter()
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Be(0);
        usage.ToString().Should().Contain("order trigger Place \"places {subject}\": 1");
        (
            await MachineCommand
                .RunMissingAsync(
                    Features,
                    glossaryPath: null,
                    [],
                    outPath: null,
                    new StringWriter(),
                    new StringWriter()
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Be(0);
    }

    [Test]
    public async Task It_writes_one_ir_per_machine_the_host_file_and_the_access_report()
    {
        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(0, err);
        Directory
            .EnumerateFiles(Out)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo(["order.ir.json", "shop.ir.json", "host.json", "access.txt"]);
        (await File.ReadAllTextAsync(Path.Combine(Out, "access.txt")).ConfigureAwait(false))
            .Should()
            .Contain("Place");
        (await RunAsync(check: true).ConfigureAwait(false))
            .Exit.Should()
            .Be(0, "nothing changed since it was written");
    }

    [Test]
    public async Task A_machine_that_stops_deriving_fails_the_check_then_is_removed()
    {
        await RunAsync().ConfigureAwait(false);
        File.Delete(Path.Combine(Features, "opening.feature"));

        var check = await RunAsync(check: true).ConfigureAwait(false);
        check.Exit.Should().Be(1);
        check.Err.Should().Contain("shop.ir.json no longer derives");

        (await RunAsync().ConfigureAwait(false)).Exit.Should().Be(0);
        File.Exists(Path.Combine(Out, "shop.ir.json")).Should().BeFalse();
        (await RunAsync(check: true).ConfigureAwait(false)).Exit.Should().Be(0);
    }

    [Test]
    public async Task A_changed_grant_makes_the_access_report_stale()
    {
        await RunAsync().ConfigureAwait(false);
        var glossary = Path.Combine(Features, "glossary.json");
        await File.WriteAllTextAsync(
                glossary,
                (await File.ReadAllTextAsync(glossary).ConfigureAwait(false)).Replace(
                    "\"roles\": [\"customer\"]",
                    "\"roles\": [\"shopper\"]",
                    StringComparison.Ordinal
                )
            )
            .ConfigureAwait(false);

        var check = await RunAsync(check: true).ConfigureAwait(false);

        check.Exit.Should().Be(1);
        check.Err.Should().Contain("access.txt is stale");
    }

    [Test]
    public async Task An_out_that_is_a_file_is_refused()
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "order.ir.json");
        await File.WriteAllTextAsync(file, "{}");

        var (exit, _, err) = await RunAsync(outDir: file).ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("--out is a file");
        (await File.ReadAllTextAsync(file).ConfigureAwait(false)).Should().Be("{}");
    }

    [Test]
    public async Task An_out_holding_files_it_did_not_write_is_refused_untouched()
    {
        Directory.CreateDirectory(Out);
        await File.WriteAllTextAsync(Path.Combine(Out, "notes.md"), "mine");

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("notes.md");
        Directory.EnumerateFiles(Out).Select(Path.GetFileName).Should().Equal("notes.md");
    }

    [Test]
    public async Task A_missing_glossary_is_a_message()
    {
        File.Delete(Path.Combine(Features, "glossary.json"));

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("There is no glossary at");
    }

    [Test]
    public async Task A_bad_glossary_names_its_key()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Features, "glossary.json"),
            """{ "version": 2 }"""
        );

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("glossary.json").And.Contain("version");
    }

    [Test]
    public async Task A_problem_writes_nothing()
    {
        await File.AppendAllTextAsync(
            Path.Combine(Features, "ordering.feature"),
            """

              Scenario: Nonsense
                Given Ada is a customer
                When Ada juggles her order
                Then Ada's order is placed
            """
        );

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("ordering.feature:");
        Directory.Exists(Out).Should().BeFalse();
    }

    [Test]
    public async Task Feature_paths_are_relative_so_the_output_does_not_depend_on_where_it_runs()
    {
        await RunAsync().ConfigureAwait(false);
        (await File.ReadAllTextAsync(Path.Combine(Out, "host.json")).ConfigureAwait(false))
            .Should()
            .Contain("\"file\":\"ordering.feature\"")
            .And.NotContain(_dir);
    }

    [Test]
    public async Task Run_passes_the_features_on_the_committed_machines()
    {
        await RunAsync().ConfigureAwait(false);

        var (exit, stdout, err) = await RunFeaturesAsync().ConfigureAwait(false);

        exit.Should().Be(0, err);
        stdout.Should().Contain("ordering.feature:2 Ada places her order  passed");
        stdout.Should().Contain("2 scenario(s): 2 passed");
    }

    [Test]
    public async Task Run_fails_a_scenario_the_committed_machines_answer_differently()
    {
        await RunAsync().ConfigureAwait(false);
        await File.AppendAllTextAsync(
                Path.Combine(Features, "ordering.feature"),
                """

                  Scenario: Placing twice
                    Given Ada is a customer
                    And Ada's order is placed
                    When Ada places her order
                    Then Ada's order is placed
                """
            )
            .ConfigureAwait(false);

        var (exit, stdout, _) = await RunFeaturesAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        stdout.Should().Contain("Placing twice  failed").And.Contain("NO_TRANSITION");
    }

    [Test]
    public async Task Run_selects_by_location_and_writes_junit()
    {
        await RunAsync().ConfigureAwait(false);
        var report = Path.Combine(_dir, "report.xml");

        var (exit, _, err) = await RunFeaturesAsync(
                new MachineCommand.RunArguments(
                    Features,
                    Path.Combine(Features, "glossary.json"),
                    Out,
                    ["opening.feature:2"],
                    Format: "junit",
                    ReportPath: report
                )
            )
            .ConfigureAwait(false);

        exit.Should().Be(0, err);
        var xml = await File.ReadAllTextAsync(report).ConfigureAwait(false);
        xml.Should().Contain("The shop opens (line 2)").And.NotContain("Ada places her order");
    }

    [Test]
    public async Task Run_writes_junit_for_names_and_steps_xml_cannot_hold()
    {
        await RunAsync().ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(Features, "odd.feature"),
                "Feature: Odd\n"
                    + "  Scenario: The shop\u000Bopens\n"
                    + "    Given Kit is a baker\n"
                    + "    And the shop is closed\n"
                    + "    When the shop opens\n"
                    + "    Then the shop\u000Bis closed\n"
            )
            .ConfigureAwait(false);
        var report = Path.Combine(_dir, "report.xml");

        var (exit, _, err) = await RunFeaturesAsync(
                new MachineCommand.RunArguments(
                    Features,
                    Path.Combine(Features, "glossary.json"),
                    Out,
                    ["odd.feature:2"],
                    Format: "junit",
                    ReportPath: report
                )
            )
            .ConfigureAwait(false);

        err.Should().NotContain("Exception");
        File.Exists(report).Should().BeTrue(err);
        exit.Should().Be(1, "the shop is open, not closed");
        var xml = await File.ReadAllTextAsync(report).ConfigureAwait(false);
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
    public async Task Run_refuses_a_machines_directory_whose_host_file_was_derived_from_other_irs()
    {
        await RunAsync().ConfigureAwait(false);
        var ir = Path.Combine(Out, "order.ir.json");
        await File.WriteAllTextAsync(
                ir,
                (await File.ReadAllTextAsync(ir).ConfigureAwait(false)).Replace(
                    "\"Placed\"",
                    "\"Sent\"",
                    StringComparison.Ordinal
                )
            )
            .ConfigureAwait(false);

        var (exit, _, err) = await RunFeaturesAsync().ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("order");
    }

    [Test]
    public async Task Run_refuses_a_bad_tag_expression_and_a_bad_location()
    {
        await RunAsync().ConfigureAwait(false);

        (
            await RunFeaturesAsync(
                    new MachineCommand.RunArguments(
                        Features,
                        Path.Combine(Features, "glossary.json"),
                        Out,
                        [],
                        Tags: "@a and"
                    )
                )
                .ConfigureAwait(false)
        )
            .Err.Should()
            .Contain("--tags");
        (
            await RunFeaturesAsync(
                    new MachineCommand.RunArguments(
                        Features,
                        Path.Combine(Features, "glossary.json"),
                        Out,
                        ["ordering"]
                    )
                )
                .ConfigureAwait(false)
        )
            .Err.Should()
            .Contain("is not file:line");
    }

    [Test]
    public async Task Run_fails_when_no_scenario_matches()
    {
        await RunAsync().ConfigureAwait(false);
        var glossary = Path.Combine(Features, "glossary.json");

        var tag = await RunFeaturesAsync(
                new MachineCommand.RunArguments(Features, glossary, Out, [], Tags: "@smok")
            )
            .ConfigureAwait(false);
        var location = await RunFeaturesAsync(
                new MachineCommand.RunArguments(Features, glossary, Out, ["ordering.feature:99"])
            )
            .ConfigureAwait(false);

        tag.Exit.Should().Be(1, "a selection that runs nothing is not a pass");
        tag.Err.Should().Contain("No scenario matched");
        location.Exit.Should().Be(1, "a location that names no scenario is not a pass");
        location.Err.Should().Contain("No scenario matched");
    }

    [Test]
    public async Task Run_fails_a_selection_whose_every_scenario_is_host_only()
    {
        await File.WriteAllTextAsync(
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
        await RunAsync().ConfigureAwait(false);
        var glossary = Path.Combine(Features, "glossary.json");

        var hostOnly = await RunFeaturesAsync(
                new MachineCommand.RunArguments(Features, glossary, Out, [], Tags: "@host")
            )
            .ConfigureAwait(false);
        var all = await RunFeaturesAsync().ConfigureAwait(false);

        hostOnly.Exit.Should().Be(1, "a selection that runs nothing is not a pass");
        hostOnly.Err.Should().Contain("tagged @host").And.Contain("nothing ran");
        all.Exit.Should().Be(0, all.Err + all.Out);
    }

    [Test]
    public async Task Run_refuses_a_name_that_takes_too_long_to_match()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Features, "opening.feature"),
            Opening.Replace(
                "Scenario: The shop opens",
                "Scenario: " + new string('a', 40) + "!",
                StringComparison.Ordinal
            )
        );
        await RunAsync().ConfigureAwait(false);

        var (exit, _, err) = await RunFeaturesAsync(
                new MachineCommand.RunArguments(
                    Features,
                    Path.Combine(Features, "glossary.json"),
                    Out,
                    [],
                    Names: ["(a+)+$"]
                )
            )
            .ConfigureAwait(false);

        exit.Should().Be(1);
        err.Should().Contain("--name '(a+)+$'");
    }

    [Test]
    public async Task Check_and_write_read_a_crlf_checkout_as_unchanged()
    {
        await RunAsync().ConfigureAwait(false);
        foreach (var path in Directory.EnumerateFiles(Out))
            await File.WriteAllTextAsync(
                    path,
                    (await File.ReadAllTextAsync(path).ConfigureAwait(false)).Replace(
                        "\n",
                        "\r\n",
                        StringComparison.Ordinal
                    )
                )
                .ConfigureAwait(false);

        var check = await RunAsync(check: true).ConfigureAwait(false);
        var write = await RunAsync().ConfigureAwait(false);

        check.Exit.Should().Be(0, check.Err);
        write.Exit.Should().Be(0, write.Err);
        write.Out.Should().NotContain("updated", "a CRLF checkout is not a change to write");
    }

    [Test]
    public async Task Missing_lists_each_gap_with_a_stub_and_writes_them_to_paste()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Features, "glossary.json"),
            Glossary.Replace(
                "\"Place\": { \"phrase\": \"places {subject}\" }",
                "\"Place\": { \"phrase\": \"places {subject}\" }, \"Cancel\": { \"phrase\": \"cancels {subject}\" }",
                StringComparison.Ordinal
            )
        );
        var stubs = Path.Combine(_dir, "missing.feature");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await MachineCommand
            .RunMissingAsync(
                Features,
                Path.Combine(Features, "glossary.json"),
                ["trigger"],
                stubs,
                stdout,
                stderr
            )
            .ConfigureAwait(false);

        exit.Should().Be(0, stderr.ToString());
        stdout
            .ToString()
            .Should()
            .Contain("# order Cancel: no scenario fires it")
            .And.Contain("1 gap(s).");
        (await File.ReadAllTextAsync(stubs).ConfigureAwait(false))
            .Should()
            .StartWith("Feature: Missing scenarios")
            .And.Contain("When Ada cancels their order");
    }

    [Test]
    public async Task Usage_lists_each_phrase_with_the_steps_that_say_it()
    {
        var stdout = new StringWriter();

        var exit = await MachineCommand
            .RunUsageAsync(
                Features,
                Path.Combine(Features, "glossary.json"),
                unusedOnly: false,
                stdout,
                new StringWriter()
            )
            .ConfigureAwait(false);

        exit.Should().Be(0);
        stdout
            .ToString()
            .Should()
            .Contain("order trigger Place \"places {subject}\": 1\n  ordering.feature:5");
    }

    [Test]
    public async Task Usage_reports_a_feature_it_cannot_read()
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
            var exit = await MachineCommand
                .RunUsageAsync(
                    Features,
                    Path.Combine(Features, "glossary.json"),
                    unusedOnly: false,
                    new StringWriter(),
                    stderr
                )
                .ConfigureAwait(false);

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
    public async Task Diagram_draws_the_board_with_the_scenarios_behind_each_sticky(string format)
    {
        await RunAsync().ConfigureAwait(false);
        var stdout = new StringWriter();

        var exit = await MachineCommand
            .RunDiagramAsync(Out, format, outPath: null, stdout, new StringWriter())
            .ConfigureAwait(false);

        exit.Should().Be(0);
        stdout.ToString().Should().Contain("Place");
        if (!string.Equals(format, "html", StringComparison.Ordinal))
            stdout.ToString().Should().Contain("Draft → Placed");
        if (!string.Equals(format, "mermaid", StringComparison.Ordinal))
            stdout.ToString().Should().Contain("Ada places her order");
    }

    [Test]
    public async Task The_html_board_never_parses_a_label_as_markup()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Features, "ordering.feature"),
            Ordering.Replace(
                "Scenario: Ada places her order",
                "Scenario: Ada places </script><b>x</b>",
                StringComparison.Ordinal
            )
        );
        await RunAsync().ConfigureAwait(false);
        var stdout = new StringWriter();

        (
            await MachineCommand
                .RunDiagramAsync(Out, "html", outPath: null, stdout, new StringWriter())
                .ConfigureAwait(false)
        )
            .Should()
            .Be(0);

        var html = stdout.ToString();
        html.Should().NotContain("</script><b>").And.NotContain("innerHTML");
        html.Should().Contain("\\u003C/script\\u003E");
    }

    [Test]
    public async Task The_mermaid_board_quotes_each_machine_and_keeps_each_label_on_its_line()
    {
        await File.WriteAllTextAsync(
            Path.Combine(Features, "glossary.json"),
            Glossary
                .Replace("\"shop\": {", "\"end\": {", StringComparison.Ordinal)
                .Replace("shop/main", "end/main", StringComparison.Ordinal)
                .Replace("\"order\": {", "\"n0\": {", StringComparison.Ordinal)
                .Replace(
                    "\"roles\": [\"customer\"]",
                    "\"roles\": [\"cus\\ntomer#1\"]",
                    StringComparison.Ordinal
                )
        );
        var derived = await RunAsync().ConfigureAwait(false);
        derived.Exit.Should().Be(0, derived.Err);
        var stdout = new StringWriter();

        (
            await MachineCommand
                .RunDiagramAsync(Out, "mermaid", outPath: null, stdout, new StringWriter())
                .ConfigureAwait(false)
        )
            .Should()
            .Be(0);

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
    public async Task Diagram_refuses_a_directory_with_no_host_file()
    {
        var stderr = new StringWriter();

        (
            await MachineCommand
                .RunDiagramAsync(Features, "json", outPath: null, new StringWriter(), stderr)
                .ConfigureAwait(false)
        )
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
    public async Task A_warning_is_printed_to_stderr_and_fails_neither_the_write_nor_the_check()
    {
        await File.WriteAllTextAsync(Path.Combine(Features, "glossary.json"), Quiz);
        File.Delete(Path.Combine(Features, "ordering.feature"));
        File.Delete(Path.Combine(Features, "opening.feature"));
        await File.WriteAllTextAsync(Path.Combine(Features, "quiz.feature"), Sheets);

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);
        var (checkExit, checkOut, checkErr) = await RunAsync(check: true).ConfigureAwait(false);

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

    [Test]
    public async Task Explain_prints_a_warning_to_stderr_and_still_explains()
    {
        await File.WriteAllTextAsync(Path.Combine(Features, "glossary.json"), Quiz);
        File.Delete(Path.Combine(Features, "ordering.feature"));
        File.Delete(Path.Combine(Features, "opening.feature"));
        await File.WriteAllTextAsync(Path.Combine(Features, "quiz.feature"), Sheets);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await MachineCommand
            .RunExplainAsync(
                Features,
                Path.Combine(Features, "glossary.json"),
                scenario: null,
                stdout,
                stderr
            )
            .ConfigureAwait(false);

        exit.Should().Be(0, stderr.ToString());
        stderr
            .ToString()
            .Should()
            .StartWith(
                "warning: quiz.feature:52: When quiz enters Between, the policy quiz.Between>answer-sheet.Clear"
            );
        stdout.ToString().Should().NotContain("warning:").And.Contain("quiz.feature:");
    }

    // On a file system that ignores case, writing checkout.ir.json writes into a Checkout.ir.json already there, and
    // removing that one as no longer derived would remove what was just written. On one that keeps case, it is
    // another file and is removed. Either way the directory ends holding the derived names, spelled as derived.
    [Test]
    public async Task An_ir_already_there_in_another_case_ends_spelled_as_derived()
    {
        (await RunAsync().ConfigureAwait(false)).Exit.Should().Be(0);
        var derived = Path.Combine(Out, "order.ir.json");
        var text = await File.ReadAllTextAsync(derived).ConfigureAwait(false);
        File.Delete(derived);
        await File.WriteAllTextAsync(Path.Combine(Out, "Order.ir.json"), text)
            .ConfigureAwait(false);

        var (exit, _, err) = await RunAsync().ConfigureAwait(false);

        exit.Should().Be(0, err);
        Directory
            .EnumerateFiles(Out)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo(["order.ir.json", "shop.ir.json", "host.json", "access.txt"]);
        (await RunAsync(check: true).ConfigureAwait(false)).Exit.Should().Be(0);
    }

    private async Task<(int Exit, string Out, string Err)> RunFeaturesAsync(
        MachineCommand.RunArguments? args = null
    )
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MachineCommand
            .RunRunAsync(
                args
                    ?? new MachineCommand.RunArguments(
                        Features,
                        Path.Combine(Features, "glossary.json"),
                        Out,
                        []
                    ),
                stdout,
                stderr
            )
            .ConfigureAwait(false);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    // The features directory written with a lexicon in place of glossary.json.
    private async Task UseLexiconAsync(string text = Lexicon)
    {
        File.Delete(Path.Combine(Features, "glossary.json"));
        await File.WriteAllTextAsync(Path.Combine(Features, "shop.lexicon"), text)
            .ConfigureAwait(false);
    }

    private async Task<(int Exit, string Out, string Err)> FromLexiconAsync(bool check = false)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MachineCommand
            .RunFromGherkinAsync(Features, glossaryPath: null, Out, check, stdout, stderr)
            .ConfigureAwait(false);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private async Task<(int Exit, string Out, string Err)> RunAsync(
        bool check = false,
        string? outDir = null,
        string? glossary = null,
        IReadOnlyList<string>? trains = null
    )
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MachineCommand
            .RunFromGherkinAsync(
                Features,
                glossary ?? Path.Combine(Features, "glossary.json"),
                outDir ?? Out,
                check,
                stdout,
                stderr,
                trains
            )
            .ConfigureAwait(false);
        return (exit, stdout.ToString(), stderr.ToString());
    }
}
