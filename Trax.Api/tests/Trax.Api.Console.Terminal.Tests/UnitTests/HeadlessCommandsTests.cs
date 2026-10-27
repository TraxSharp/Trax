using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Headless;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Tests.Fakes;
using Trax.Api.Console.Terminal.Trace;
using Trax.Client;

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

    private const string Operation = "0f8e7d6c-5b4a-4392-8170-6f5e4d3c2b1a";

    private TimeSpan _poll;

    [SetUp]
    public void FastPolling()
    {
        _poll = HeadlessCommands.BulkPollInterval;
        HeadlessCommands.BulkPollInterval = TimeSpan.FromMilliseconds(1);
    }

    [TearDown]
    public void Polling() => HeadlessCommands.BulkPollInterval = _poll;

    [Test]
    public async Task Do_with_dry_run_prints_what_it_would_send_and_sends_nothing()
    {
        var host = Hosts.Same();

        var (exit, output, _) = await Hosts.RunAsync(
            host,
            "do",
            "dead-letters",
            "requeue",
            "7",
            "9",
            "--dry-run"
        );

        exit.Should().Be(0);
        var line = JsonNode.Parse(output)!;
        line["dryRun"]!.GetValue<bool>().Should().BeTrue();
        line["ids"]!.AsArray().Select(i => i!.GetValue<long>()).Should().Equal(7, 9);
        host.Sent("RequeueDeadLetters").Should().BeEmpty();
    }

    [Test]
    public async Task Do_prints_what_the_host_said_and_exits_0_when_it_took()
    {
        var host = Hosts
            .Same()
            .Answer(
                "RequeueDeadLetters",
                """{"operations":{"deadLetters":{"requeueDeadLetters":{"count":2,"message":"Requeued 2."}}}}"""
            );

        var (exit, output, error) = await Hosts.RunAsync(
            host,
            "do",
            "dead-letters",
            "requeue",
            "7",
            "9",
            "--ask-afresh"
        );

        exit.Should().Be(0, error);
        var line = JsonNode.Parse(output)!;
        line["succeeded"]!.GetValue<bool>().Should().BeTrue();
        line["message"]!.GetValue<string>().Should().Be("Requeued 2.");
        var sent = host.Sent("RequeueDeadLetters").Single();
        sent["ids"]!.AsArray().Select(i => i!.GetValue<long>()).Should().Equal(7, 9);
        sent["askAfresh"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task Do_that_the_host_refuses_exits_1_with_its_code()
    {
        var host = new RefusingHost(Hosts.Same()).Refuse(
            "RequeueDeadLetters",
            "Not an operator.",
            "AUTH_NOT_AUTHORIZED"
        );

        var (exit, output, _) = await Hosts.RunAsync(host, "do", "dead-letters", "requeue", "7");

        exit.Should().Be(1);
        var line = JsonNode.Parse(output)!;
        line["succeeded"]!.GetValue<bool>().Should().BeFalse();
        line["code"]!.GetValue<string>().Should().Be("AUTH_NOT_AUTHORIZED");
    }

    [Test]
    public async Task Bulk_with_dry_run_prints_the_filter_it_would_send()
    {
        var host = Hosts.Same();

        var (exit, output, _) = await Hosts.RunAsync(
            host,
            "bulk",
            "dead-letters",
            "requeue",
            "--where",
            "status=awaiting_intervention",
            "--expect",
            "3",
            "--dry-run"
        );

        exit.Should().Be(0);
        var line = JsonNode.Parse(output)!;
        line["dryRun"]!.GetValue<bool>().Should().BeTrue();
        line["filter"]!["deadLetters"]!["status"]!
            .GetValue<string>()
            .Should()
            .Be("AWAITING_INTERVENTION");
        host.Sent("StartBulkOperation").Should().BeEmpty();
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    public async Task Bulk_prints_its_progress_until_it_ends(int failed, int exitCode)
    {
        var host = Bulk(Started(success: true), "RUNNING", 1, 0, "FINISHED", 3, failed);

        var (exit, output, error) = await Hosts.RunAsync(
            host,
            "bulk",
            "dead-letters",
            "requeue",
            "--where",
            "status=awaiting_intervention",
            "--expect",
            "3"
        );

        exit.Should().Be(exitCode, error);
        var lines = Lines(output);
        lines[0]["operation"]!.GetValue<string>().Should().Be(Operation);
        lines
            .Skip(1)
            .Select(l => l["status"]!.GetValue<string>())
            .Should()
            .Equal("RUNNING", "FINISHED");
        lines[^1]["failed"]!.GetValue<int>().Should().Be(failed);
        var sent = host.Sent("StartBulkOperation").Single();
        sent["expectedCount"]!.GetValue<int>().Should().Be(3);
        sent["kind"]!.GetValue<string>().Should().Be("REQUEUE");
    }

    [Test]
    public async Task Bulk_the_host_refuses_to_start_exits_1_and_reads_no_progress()
    {
        var host = Bulk(Started(success: false));

        var (exit, output, _) = await Hosts.RunAsync(
            host,
            "bulk",
            "dead-letters",
            "requeue",
            "--expect",
            "3"
        );

        exit.Should().Be(1);
        JsonNode.Parse(output)!["succeeded"]!.GetValue<bool>().Should().BeFalse();
        host.Sent("BulkOperation").Should().BeEmpty();
    }

    [Test]
    public async Task Watch_prints_each_event_and_exits_0_when_the_host_ends_the_stream()
    {
        var host = new StreamingHost(Hosts.Same());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = Hosts.RunAsync(host, stop.Token, "watch", "changes");

        var stream = await host.StreamAsync("OnDataChanged");
        stream.Next(Changed());
        stream.Complete();
        var (exit, output, error) = await run;

        exit.Should().Be(0, error);
        Lines(output).Single()["domain"]!.GetValue<string>().Should().Be("DEAD_LETTER");
    }

    [Test]
    public async Task Watch_exits_1_when_the_host_ends_the_stream_with_an_error()
    {
        var host = new StreamingHost(Hosts.Same());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = Hosts.RunAsync(host, stop.Token, "watch", "changes");

        (await host.StreamAsync("OnDataChanged")).Error(
            new SubscriptionErrorException([
                new JsonObject
                {
                    ["message"] = "Not an operator.",
                    ["extensions"] = new JsonObject { ["code"] = "AUTH_NOT_AUTHORIZED" },
                },
            ])
        );
        var (exit, _, error) = await run;

        exit.Should().Be(1);
        JsonNode.Parse(error)!["code"]!.GetValue<string>().Should().Be("AUTH_NOT_AUTHORIZED");
    }

    [Test]
    public async Task Watch_says_when_the_stream_was_interrupted_and_ctrl_c_exits_0()
    {
        var host = new StreamingHost(Hosts.Same());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = Hosts.RunAsync(host, stop.Token, "watch", "changes");

        (await host.StreamAsync("OnDataChanged")).Error(new IOException("Connection reset."));
        await WaitAsync(() => run.IsCompleted || Interrupted(host));
        stop.Cancel();
        var (exit, _, error) = await run.WaitAsync(TimeSpan.FromSeconds(10));

        exit.Should().Be(0);
        Lines(error)
            .Should()
            .Contain(l => l["interrupted"] != null)
            .And.Contain(l => l["resumed"] != null);
    }

    [Test]
    public async Task Watch_of_a_stream_that_does_not_exist_exits_2()
    {
        var (exit, _, error) = await Hosts.RunAsync(Hosts.Same(), "watch", "everything");

        exit.Should().Be(2);
        error.Should().Contain("is not a stream");
    }

    [Test]
    public async Task Graph_of_a_machine_the_host_does_not_serve_exits_2()
    {
        var (exit, _, error) = await Hosts.RunAsync(Hosts.Same(), "graph", "checkout");

        exit.Should().Be(2);
        error.Should().Contain("serves no machine 'checkout'");
    }

    // A bulk operation applies to the rows the list shows under the same filters: each filter the list takes sends
    // the same value, under the same name, in the list's query and in the bulk operation's filter.
    [Test]
    public async Task Each_lists_bulk_filter_matches_the_query_it_reads_with()
    {
        foreach (var list in Catalog.All.Where(l => l.BulkActions.Count > 0))
        {
            var filters = list.Filters.ToDictionary(f => f.Name, Sample, StringComparer.Ordinal);
            var host = new FakeHost();
            await list.LoadAsync(
                new TraxClient(host),
                new ListQuery(25, null, filters),
                CancellationToken.None
            );
            var query = host.Requests.Single().Variables;
            query.Remove("take");
            query.Remove("afterId");

            foreach (var bulk in list.BulkActions)
            {
                var filter = (JsonObject)Wire.Json(bulk.Filter(filters))!;
                var set = (JsonObject)filter.Single(p => p.Value is not null).Value!;

                set.Select(p => p.Key)
                    .Should()
                    .BeEquivalentTo(
                        query.Select(p => p.Key),
                        $"{list.Name} {bulk.Name} filters by every filter"
                    );
                foreach (var (name, value) in query)
                    JsonNode
                        .DeepEquals(set[name], value)
                        .Should()
                        .BeTrue($"{list.Name} {bulk.Name} sends {name} as the list does");
            }
        }
    }

    private static string Sample(ListFilter filter) =>
        filter.Choices is { } choices ? choices[^1]
        : filter.Name is "manifest" or "parent" or "group" ? "42"
        : "Shop.ISomeTrain";

    private static bool Interrupted(StreamingHost host) => host.Interrupted;

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition never held.");
            // allowed-delay: the polling interval of a wait with a deadline.
            await Task.Delay(10);
        }
    }

    private static JsonObject Changed() =>
        new()
        {
            ["data"] = new JsonObject
            {
                ["onDataChanged"] = new JsonObject
                {
                    ["domain"] = "DEAD_LETTER",
                    ["timestamp"] = "2026-10-22T10:00:00Z",
                },
            },
        };

    private static string Started(bool success) =>
        success
            ? """
                {"operations":{"startBulkOperation":{"success":true,"message":"Started.","matched":3,"operation":
                  {"id":"OPERATION","kind":"REQUEUE","status":"RUNNING","actor":"op","startedAt":"2026-10-22T10:00:00Z",
                   "total":3,"done":0,"failed":0,"message":"Running."}}}}
                """.Replace("OPERATION", Operation)
            : """{"operations":{"startBulkOperation":{"success":false,"message":"The filter now matches 4.","matched":4,"operation":null}}}""";

    // A host that starts a bulk operation and reports it through the states given, (status, done, failed) each.
    private static FakeHost Bulk(string started, params object[] states)
    {
        var read = 0;
        return Hosts
            .Same()
            .Answer("StartBulkOperation", started)
            .Answer(
                "BulkOperation",
                _ =>
                {
                    var at =
                        Math.Min(Interlocked.Increment(ref read) - 1, states.Length / 3 - 1) * 3;
                    return (JsonObject)
                        JsonNode.Parse(
                            """
                            {"operations":{"bulkOperation":{"id":"OPERATION","kind":"REQUEUE","status":"STATUS",
                              "actor":"op","askAfresh":false,"startedAt":"2026-10-22T10:00:00Z","finishedAt":null,
                              "total":3,"done":DONE,"failed":FAILED,"message":"Done so far.",
                              "filter":{"executions":null,"deadLetters":null,"manifests":null},"failureReasons":[],"failures":[]}}}
                            """.Replace("OPERATION", Operation).Replace(
                                "DONE",
                                $"{states[at + 1]}"
                            ).Replace("FAILED", $"{states[at + 2]}").Replace(
                                "STATUS",
                                $"{states[at]}"
                            )
                        )!;
                }
            );
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
