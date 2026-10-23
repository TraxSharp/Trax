using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Api.Console.Terminal.Trace;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// The headless commands against a scripted host: what each prints, one JSON value per line, and what it exits with.
/// </summary>
[TestFixture]
public class HeadlessCommandsTests
{
    [Test]
    public async Task Trace_prints_a_runs_steps_decisions_and_log()
    {
        var (exit, output, error) = await Hosts.RunAsync(Traced(steps: 2), "trace", "4812");

        exit.Should().Be(0, error);
        var lines = Lines(output);
        lines.Select(l => l["kind"]!.GetValue<string>()).Should().Equal("step", "step", "log");
        lines[0]["text"]!.GetValue<string>().Should().Contain("junction Step0");
        error.Should().BeEmpty("nothing was left out");
    }

    [Test]
    public async Task Trace_says_when_a_part_reached_its_limit()
    {
        var (exit, output, error) = await Hosts.RunAsync(
            Traced(steps: RunTrace.Take),
            "trace",
            "4812"
        );

        exit.Should().Be(0, error);
        Lines(output).Should().HaveCount(RunTrace.Take + 1);
        var marker = JsonNode.Parse(error)!;
        marker["truncated"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal("step");
        marker["limit"]!.GetValue<int>().Should().Be(RunTrace.Take);
    }

    [Test]
    public async Task Trace_of_a_refused_run_exits_1_with_the_refusal_on_standard_error()
    {
        var host = new RefusingHost(Hosts.Same()).Refuse(
            "JunctionRuns",
            "No such run.",
            "NOT_FOUND"
        );

        var (exit, output, error) = await Hosts.RunAsync(host, "trace", "4812");

        exit.Should().Be(1);
        output.Should().BeEmpty();
        JsonNode.Parse(error)!["code"]!.GetValue<string>().Should().Be("NOT_FOUND");
    }

    private static FakeHost Traced(int steps) =>
        Hosts
            .Same()
            .Answer(
                "JunctionRuns",
                new JsonObject
                {
                    ["operations"] = new JsonObject
                    {
                        ["junctionRuns"] = new JsonArray(
                            Enumerable
                                .Range(0, steps)
                                .Select(i =>
                                    (JsonNode)
                                        new JsonObject
                                        {
                                            ["position"] = i,
                                            ["kind"] = "JUNCTION",
                                            ["name"] = $"Step{i}",
                                            ["state"] = "COMPLETED",
                                            ["startedAt"] = DateTimeOffset
                                                .Parse("2026-10-22T10:00:00Z")
                                                .AddSeconds(i)
                                                .ToString("O"),
                                            ["replayed"] = false,
                                            ["answerWithheld"] = false,
                                            ["nameWithheld"] = false,
                                        }
                                )
                                .ToArray()
                        ),
                    },
                }.ToJsonString()
            )
            .Answer(
                "Decisions",
                """{"operations":{"decisions":{"items":[],"take":500,"nextCursor":null}}}"""
            )
            .Answer(
                "Logs",
                """
                {"operations":{"logs":{"logs":{"items":[
                  {"id":1,"metadataId":4812,"eventId":0,"level":"INFORMATION","category":"Shop","message":"Charged.","exception":null}
                ],"totalCount":1,"isEstimatedCount":false,"isCountCapped":false,"nextCursor":null}}}}
                """
            );

    private static List<JsonNode> Lines(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonNode.Parse(l)!)
            .ToList();
}
