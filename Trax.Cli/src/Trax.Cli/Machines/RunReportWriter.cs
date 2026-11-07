using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Machines;

/// <summary>
/// Writes a run's report for <c>trax machine run</c>: <c>pretty</c> for a terminal, <c>json</c> for tools, and
/// <c>junit</c> for CI test views. Every format lists scenarios in file and line order, so two runs of the same features
/// write the same bytes.
/// </summary>
internal static class RunReportWriter
{
    internal static readonly string[] Formats = ["pretty", "json", "junit"];

    internal static string Write(RunReport report, string format) =>
        format switch
        {
            "pretty" => Pretty(report),
            "json" => Json(report),
            "junit" => JUnit(report),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };

    private static string Status(ScenarioStatus? status) =>
        status switch
        {
            ScenarioStatus.Passed => "passed",
            ScenarioStatus.Failed => "failed",
            ScenarioStatus.Undefined => "undefined",
            _ => "skipped",
        };

    private static string Pretty(RunReport report)
    {
        var text = new System.Text.StringBuilder();
        foreach (var scenario in report.Scenarios)
        {
            var s = scenario.Scenario;
            text.Append($"{s.File}:{s.Line} {s.Name}  {Status(scenario.Status)}\n");
            if (scenario.Failure is { } failure)
                text.Append(
                        $"  {s.File}:{failure.Step.Line} {failure.Step.Kind} {failure.Step.Text}\n"
                    )
                    .Append($"    {failure.Message}\n");
        }
        var counts = report
            .Scenarios.GroupBy(s => Status(s.Status))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");
        text.Append($"{report.Scenarios.Count} scenario(s): {string.Join(", ", counts)}\n");
        var skipped = report.Scenarios.Sum(s => s.Skipped.Count(x => x.Message != "not run"));
        if (skipped > 0)
            text.Append($"{skipped} step(s) about the UI, not checked here\n");
        return text.ToString();
    }

    private static string Json(RunReport report)
    {
        var scenarios = new JsonArray();
        foreach (var scenario in report.Scenarios)
        {
            var steps = new JsonArray();
            foreach (var step in scenario.Steps)
            {
                var written = new JsonObject
                {
                    ["line"] = step.Step.Line,
                    ["keyword"] = step.Step.Kind.ToString(),
                    ["text"] = step.Step.Text,
                    ["status"] = Status(step.Status),
                };
                if (step.Message is { } message && step.Status is not ScenarioStatus.Passed)
                    written["message"] = message;
                steps.Add(written);
            }
            scenarios.Add(
                new JsonObject
                {
                    ["file"] = scenario.Scenario.File,
                    ["line"] = scenario.Scenario.Line,
                    ["name"] = scenario.Scenario.Name,
                    ["tags"] = new JsonArray(
                        scenario.Scenario.Tags.Select(t => (JsonNode?)t).ToArray()
                    ),
                    ["status"] = Status(scenario.Status),
                    ["steps"] = steps,
                }
            );
        }
        return new JsonObject { ["scenarios"] = scenarios }.ToJsonString(
                new JsonSerializerOptions { WriteIndented = true }
            ) + "\n";
    }

    private static string JUnit(RunReport report)
    {
        var suites = new XElement("testsuites");
        foreach (var file in report.Scenarios.GroupBy(s => s.Scenario.File))
        {
            var suite = new XElement(
                "testsuite",
                new XAttribute("name", file.Key),
                new XAttribute("tests", file.Count()),
                new XAttribute("failures", file.Count(s => s.Status == ScenarioStatus.Failed)),
                new XAttribute("errors", file.Count(s => s.Status == ScenarioStatus.Undefined))
            );
            foreach (var scenario in file)
            {
                var test = new XElement(
                    "testcase",
                    new XAttribute("classname", file.Key),
                    new XAttribute(
                        "name",
                        $"{scenario.Scenario.Name} (line {scenario.Scenario.Line})"
                    )
                );
                if (scenario.Failure is { } failure)
                    test.Add(
                        new XElement(
                            scenario.Status == ScenarioStatus.Undefined ? "error" : "failure",
                            new XAttribute("message", failure.Message ?? ""),
                            $"{file.Key}:{failure.Step.Line} {failure.Step.Kind} {failure.Step.Text}"
                        )
                    );
                suite.Add(test);
            }
            suites.Add(suite);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), suites).Declaration
            + "\n"
            + suites
            + "\n";
    }
}
