using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// Holds <c>conformance/testees.json</c>, the roster of language folders and the testee each kit runs them through, to
/// the folders under <c>Trax.StateMachine/</c>, the suites in <c>.github/ci/packages.json</c> and the jobs in
/// <c>.github/workflows/ci.yml</c>, so a language folder cannot land without CI running it through the kits.
/// Marked CrossEngine because that suite, <c>state-machine-conformance</c>, is the one CI runs for a change anywhere in
/// the folder: a new language folder alone runs no other.
/// </summary>
[Category(Categories.CrossEngine)]
public partial class TesteeRosterTests
{
    private static readonly string[] NotLanguages = ["conformance", "docs"];
    private static readonly string[] Kits = ["engine", "client", "operations"];

    private static string RepositoryRoot => Directory.GetParent(FixturePaths.FolderRoot)!.FullName;

    // The three files are read once for the fixture; each call below parses its own copy.
    private static string? _testees;
    private static string? _ciYml;
    private static string? _packages;

    [OneTimeSetUp]
    public async Task ReadFiles()
    {
        _testees = await File.ReadAllTextAsync(
                Path.Combine(FixturePaths.ConformanceRoot, "testees.json")
            )
            .ConfigureAwait(false);
        _ciYml = await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot, ".github", "workflows", "ci.yml")
            )
            .ConfigureAwait(false);
        _packages = await File.ReadAllTextAsync(
                Path.Combine(RepositoryRoot, ".github", "ci", "packages.json")
            )
            .ConfigureAwait(false);
    }

    private static JsonArray Rows() => JsonNode.Parse(_testees.NotNull())!["languages"]!.AsArray();

    private static string Text(JsonNode row, string key) => (string)row[key]!;

    private static string CiYml() => _ciYml.NotNull();

    private static JsonObject Suite(string name) =>
        JsonNode.Parse(_packages.NotNull())!["suites"]!
            .AsArray()
            .Select(s => s!.AsObject())
            .SingleOrDefault(s => string.Equals((string)s["name"]!, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"packages.json has no suite named {name}.");

    [Test]
    public void Every_language_folder_has_a_row_and_every_row_a_folder()
    {
        var folders = Directory
            .GetDirectories(FixturePaths.FolderRoot)
            .Select(Path.GetFileName)
            .Where(d => !d!.StartsWith('.') && !NotLanguages.Contains(d))
            .Order(StringComparer.Ordinal)
            .ToList();
        var rows = Rows().Select(r => Text(r!, "folder")).ToList();

        rows.Should().OnlyHaveUniqueItems();
        Rows().Select(r => Text(r!, "name")).Should().OnlyHaveUniqueItems();
        rows.Order(StringComparer.Ordinal)
            .Should()
            .Equal(
                folders,
                "each directory under Trax.StateMachine/ but conformance and docs is a language"
            );
    }

    [Test]
    public void Every_row_names_one_column_of_the_client_surface_tables_and_every_column_a_row()
    {
        var protocol = File.ReadAllText(
            Path.Combine(FixturePaths.ConformanceRoot, "client", "PROTOCOL.md")
        );
        var headers = SurfaceHeader
            .Matches(protocol)
            .Select(m =>
                m.Groups["columns"].Value.Split('|', StringSplitOptions.TrimEntries).ToList()
            )
            .ToList();
        headers.Should().HaveCount(2, "the client surface has a table of types and one of members");
        headers[1].Should().Equal(headers[0]);

        Rows()
            .Select(r => Text(r!, "surfaceColumn"))
            .Should()
            .BeEquivalentTo(
                headers[0],
                o => o.WithStrictOrdering(),
                "each language's client is a column"
            );
    }

    [Test]
    public void Every_testee_is_a_command_a_test_or_none_with_a_reason()
    {
        foreach (var row in Rows())
        foreach (var kit in Kits)
        {
            var testee = row![kit]?.AsObject();
            var where = $"{Text(row, "folder")}'s {kit} testee";
            testee.Should().NotBeNull(where);
            testee!
                .Select(p => p.Key)
                .Should()
                .ContainSingle(where)
                .Which.Should()
                .BeOneOf("command", "test", "none");
            ((string?)testee.Single().Value).Should().NotBeNullOrWhiteSpace(where);
        }
    }

    [Test]
    public void Every_command_names_its_testee_and_is_one_ci_runs()
    {
        var ci = CiYml();
        foreach (var row in Rows())
        foreach (var kit in Kits)
            if ((string?)row![kit]?["command"] is { } command)
            {
                command
                    .Should()
                    .Contain($" --name {Text(row, "name")} ", "the testee runs under its own list");
                ci.Should().Contain(command, $"ci.yml runs {Text(row, "folder")}'s {kit} testee");
            }
    }

    [Test]
    public void Every_test_exists_and_an_engine_or_client_test_runs_in_its_suite()
    {
        foreach (var row in Rows())
        foreach (var kit in Kits)
            if ((string?)row![kit]?["test"] is { } test)
            {
                var path = Path.Combine(RepositoryRoot, test);
                File.Exists(path)
                    .Should()
                    .BeTrue($"{test} is {Text(row, "folder")}'s {kit} testee");
                if (
                    string.Equals(kit, "operations", StringComparison.Ordinal)
                    || !test.EndsWith(".cs", StringComparison.Ordinal)
                )
                    continue;
                // The suite runs only the CrossEngine tests of its project.
                var suite = Suite(Text(row, "suite"));
                ((string?)suite["dotnet"]?["test-args"])
                    .Should()
                    .Contain("TestCategory=CrossEngine");
                File.ReadAllText(path).Should().Contain("[Category(Categories.CrossEngine)]", test);
            }
    }

    [Test]
    public void Every_suite_exists_and_its_job_is_one_the_ci_result_needs()
    {
        var ci = CiYml();
        var jobs = Jobs(ci);
        var needs = Needs.Match(jobs["result"]);
        needs.Success.Should().BeTrue("the CI result job lists what it needs on one line");
        var gate = needs.Groups["needs"].Value.Split(',', StringSplitOptions.TrimEntries);

        foreach (var name in Rows().Select(r => Text(r!, "suite")).Distinct(StringComparer.Ordinal))
        {
            var suite = Suite(name);
            // A suite with a dotnet block runs in the suite job's matrix; one without has a job of its own that reads
            // suite-names.
            var job = suite.ContainsKey("dotnet")
                ? "suite"
                : jobs.Where(j =>
                        j.Value.Contains(
                            $"outputs.suite-names), '{name}')",
                            StringComparison.Ordinal
                        )
                    )
                    .Select(j => j.Key)
                    .SingleOrDefault();
            job.Should().NotBeNull($"a job of ci.yml runs the suite {name}");
            gate.Should().Contain(job!, $"CI result needs the job that runs {name}");
        }
    }

    [Test]
    public void Every_list_of_expected_failures_is_a_rows_and_for_a_kit_it_has_a_testee_for()
    {
        var dirs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["engine"] = Path.Combine(FixturePaths.ConformanceRoot, "expected-failures"),
            ["client"] = Path.Combine(FixturePaths.ConformanceRoot, "client", "expected-failures"),
            ["operations"] = Path.Combine(
                FixturePaths.ConformanceRoot,
                "operations",
                "expected-failures"
            ),
        };
        foreach (var (kit, dir) in dirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var row = Rows()
                    .SingleOrDefault(r =>
                        string.Equals(Text(r!, "name"), name, StringComparison.Ordinal)
                    );
                row.Should().NotBeNull($"{file} is named for a testee of testees.json");
                Path.GetExtension(file).Should().Be(".json", file);
                row![kit]!
                    ["none"]
                    .Should()
                    .BeNull($"{file} lists failures of a testee {name} has none of");
            }
        }
    }

    // The jobs of ci.yml by their key, each with its text up to the next job.
    private static Dictionary<string, string> Jobs(string ci)
    {
        var body = ci[(ci.IndexOf("\njobs:\n", StringComparison.Ordinal) + "\njobs:\n".Length)..];
        var starts = JobStart.Matches(body);
        return starts
            .Select(
                (m, i) =>
                    (
                        m.Groups["job"].Value,
                        body[m.Index..(i + 1 < starts.Count ? starts[i + 1].Index : body.Length)]
                    )
            )
            .ToDictionary(StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"^  (?<job>[A-Za-z0-9_-]+):\s*$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex JobStart { get; }

    [GeneratedRegex(
        @"^\| (?:Type|Member) \|(?<columns>.*)\|$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex SurfaceHeader { get; }

    [GeneratedRegex(
        @"^    needs: \[(?<needs>.*)\]$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Needs { get; }
}
