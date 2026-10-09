using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.Services.Runs;
using Trax.Api.Tests.Fakes;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.Checkpoints;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Api.Tests;

/// <summary>
/// The run graph of a checkpointing train's runs, on real runs over Postgres: a failed run's
/// nodes say where the resume check lets it resume and which hold its checkpoint, and a resumed
/// run's nodes before its resume point are restored, since it recorded no step for them. GraphQL's
/// <c>runGraph</c> and the dashboard's read (<c>RunGraphs.ReadAsync</c>) say the same.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", Adr)]
public class RunGraphResumeTests
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const string Database = "trax_api_run_graph_resumes";

    private const string Checkpoint = "Checkpoint<FetchedSources>#0";
    private const string Summarize = "SummarizeSources#0";

    private static readonly string[] BeforeTheCheckpoint =
    [
        "Seed<IDecider>#0",
        "GatherSources#0",
        "Switch<ResumeVault>#0",
        "Switch<ResumeVault>#0/EastWing/OpenEastWing#0",
        "Switch<ResumeVault>#0/WestWing/OpenWestWing#0",
        "FetchSources#0",
    ];

    private ResumeHost _host = null!;

    [OneTimeSetUp]
    public async Task StartHost() => _host = await ResumeHost.StartAsync(Database);

    [OneTimeTearDown]
    public async Task StopHost() => await _host.DisposeAsync();

    [SetUp]
    public void ResetProbe() => ResumeProbe.Reset();

    [Test]
    public async Task A_failed_runs_nodes_say_where_it_can_resume_on_both_surfaces()
    {
        var run = await _host.RunAsync<IResumableTrain>(nameof(SummarizeSources));

        var dashboard = await DashboardGraphAsync(run);
        var graphQl = await GraphQLGraphAsync(run);

        dashboard.CanResume.Should().BeTrue("the run can resume after its checkpoint");
        var nodes = Flatten(dashboard.Nodes).ToList();
        nodes
            .Where(n => n.CanResume)
            .Select(n => n.Id)
            .Should()
            .Equal(
                [Summarize],
                "the step after the checkpoint is the only one whose inputs a resume restores "
                    + $"or produces ({Adr})"
            );
        nodes.Where(n => n.Checkpointed).Select(n => n.Id).Should().Equal(Checkpoint);
        nodes
            .Single(n => n.Id == Checkpoint)
            .State.Should()
            .Be(RunNodeState.Withheld, "it comes after a sensitive route, so it stays withheld");

        // What the check says is what the operation does.
        foreach (var node in nodes)
        {
            var verdict = await Check(run, node.Id);
            node.CanResume.Should().Be(verdict.CanResume, $"{node.Id}: {verdict.Reason}");
        }

        graphQl.GetProperty("canResume").GetBoolean().Should().Be(dashboard.CanResume);
        FlattenJson(graphQl.GetProperty("nodes"))
            .Select(n =>
                (
                    n.GetProperty("id").GetString(),
                    n.GetProperty("state").GetString(),
                    n.GetProperty("canResume").GetBoolean(),
                    n.GetProperty("checkpointed").GetBoolean()
                )
            )
            .Should()
            .Equal(
                nodes.Select(n =>
                    (
                        (string?)n.Id,
                        (string?)ScreamingCase(n.State.ToString()),
                        n.CanResume,
                        n.Checkpointed
                    )
                ),
                "the API's runGraph and the dashboard's run graph read the same (central ADR 0022)"
            );
    }

    [Test]
    public async Task A_run_that_cannot_be_resumed_offers_no_resume()
    {
        var completed = await _host.RunAsync<IResumableTrain>(failIn: null);
        var early = await _host.RunAsync<IResumableTrain>(nameof(FetchSources));

        foreach (var run in new[] { completed, early })
        {
            var graph = await DashboardGraphAsync(run);
            graph.CanResume.Should().BeFalse();
            Flatten(graph.Nodes).Should().NotContain(n => n.CanResume || n.Checkpointed);
        }
    }

    [Test]
    public async Task A_resumed_runs_nodes_before_its_resume_point_are_restored()
    {
        var failed = await _host.RunAsync<IResumableTrain>(nameof(SummarizeSources));
        var queued = await _host.ResumeAsTheDashboardAsync(failed, null);
        queued.Success.Should().BeTrue(queued.Message);
        ResumeProbe.Reset();

        var resumed = await _host.RunResumeAsync<IResumableTrain>(queued.Id!.Value);

        ResumeProbe
            .Ran.Should()
            .Equal(
                [nameof(SummarizeSources), nameof(PublishSummary)],
                "the resumed run skipped every step before the checkpoint"
            );
        var graph = await DashboardGraphAsync(resumed);
        var nodes = Flatten(graph.Nodes).ToDictionary(n => n.Id);
        foreach (var id in BeforeTheCheckpoint.Append(Checkpoint))
            nodes[id]
                .State.Should()
                .Be(
                    RunNodeState.Restored,
                    $"{id} came before the point the run resumed at, and it wrote no step for it ({Adr})"
                );
        nodes[Summarize].State.Should().Be(RunNodeState.Completed);
        nodes["PublishSummary#0"].State.Should().Be(RunNodeState.Completed);
        graph.CanResume.Should().BeFalse("a run that completed has nothing left to resume");

        var graphQl = FlattenJson((await GraphQLGraphAsync(resumed)).GetProperty("nodes"))
            .ToDictionary(n => n.GetProperty("id").GetString()!);
        graphQl[Checkpoint].GetProperty("state").GetString().Should().Be("RESTORED");
        graphQl[Summarize].GetProperty("state").GetString().Should().Be("COMPLETED");

        // The run says which run it resumed, as the dashboard's "Resumes" field does.
        var execution = await _host.GraphQLAsync(
            $$"""{ operations { executionDetail(id: {{resumed}}) { resumeFrom resumeAt } } }"""
        );
        execution
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(execution.RootElement.GetRawText());
        var detail = execution
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("executionDetail");
        detail.GetProperty("resumeFrom").GetInt64().Should().Be(failed, Adr);
        detail
            .GetProperty("resumeAt")
            .ValueKind.Should()
            .Be(JsonValueKind.Null, "it resumed after the latest checkpoint");
    }

    private async Task<ResumeVerdict> Check(long run, string node)
    {
        var declared = _host
            .Services.GetRequiredService<ITrainChainGraphs>()
            .FindDeclared(typeof(IResumableTrain).FullName!)!;
        return await _host
            .Services.GetRequiredService<IRunResumes>()
            .Check(
                declared.Train,
                declared.Chain,
                declared.Input,
                declared.Output,
                run,
                node,
                CancellationToken.None
            );
    }

    private async Task<RunGraph> DashboardGraphAsync(long run)
    {
        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        var state = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == run)
            .Select(m => m.TrainState)
            .SingleAsync();
        state.Should().NotBe(TrainState.InProgress);
        return (
            await RunGraphs.ReadAsync(
                db,
                _host.Services.GetRequiredService<ITrainChainGraphs>(),
                _host.Services.GetRequiredService<IRunResumes>(),
                run,
                CancellationToken.None
            )
        )!;
    }

    private async Task<JsonElement> GraphQLGraphAsync(long run)
    {
        var response = await _host.GraphQLAsync(
            $$"""
            {
              operations {
                runGraph(metadataId: {{run}}) {
                  canResume
                  nodes { ...node tracks { nodes { ...node } } }
                }
              }
            }
            fragment node on RunGraphNode { id state canResume checkpointed }
            """
        );
        response
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(response.RootElement.GetRawText());
        return response
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("runGraph")
            .Clone();
    }

    private static IEnumerable<RunGraphNode> Flatten(IEnumerable<RunGraphNode> nodes) =>
        nodes.SelectMany(n => n.Tracks.SelectMany(t => Flatten(t.Nodes)).Prepend(n));

    private static IEnumerable<JsonElement> FlattenJson(JsonElement nodes) =>
        nodes
            .EnumerateArray()
            .SelectMany(n =>
                new[] { n }.Concat(
                    n.TryGetProperty("tracks", out var tracks)
                        ? tracks
                            .EnumerateArray()
                            .SelectMany(t => FlattenJson(t.GetProperty("nodes")))
                        : []
                )
            );

    private static string ScreamingCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString()))
            .ToUpperInvariant();
}
