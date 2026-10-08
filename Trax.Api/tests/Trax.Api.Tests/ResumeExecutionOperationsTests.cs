using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Services.Runs;
using Trax.Api.Tests.Fakes;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Checkpoints;
using Trax.Mediator.Services.ChainVerification;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// The operator's resume on its two surfaces: GraphQL's <c>resumeExecution</c> and the
/// dashboard's Resume buttons call the one <see cref="IOperationsService.ResumeExecutionAsync"/>
/// (central ADR 0022), on real runs of a checkpointing train over Postgres. Both queue the same
/// resume and refuse the same runs with the same reason; GraphQL applies the operations gate and
/// the train's <c>[TraxAuthorize]</c>, the dashboard its trusted scope. Neither surface returns a
/// checkpoint's stored state or the track a sensitive route took.
///
/// <para>Enforces Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
[Property("adr", Adr)]
public class ResumeExecutionOperationsTests
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const string Database = "trax_api_resume_operations";

    private const string Summarize = "SummarizeSources#0";
    private const string Fetch = "FetchSources#0";

    private ResumeHost _host = null!;

    [OneTimeSetUp]
    public async Task StartHost() => _host = await ResumeHost.StartAsync(Database);

    [OneTimeTearDown]
    public async Task StopHost() => await _host.DisposeAsync();

    [SetUp]
    public void ResetProbe() => ResumeProbe.Reset();

    [Test]
    public async Task A_resume_gives_the_same_result_on_both_surfaces()
    {
        foreach (var from in new[] { null, Summarize })
        {
            var viaGraphQl = await FailedRunAsync();
            var viaDashboard = await FailedRunAsync();

            var graphQl = Result(await _host.ResumeOverGraphQLAsync(viaGraphQl, from));
            var dashboard = await _host.ResumeAsTheDashboardAsync(viaDashboard, from);

            graphQl.Success.Should().BeTrue(graphQl.Message);
            dashboard.Success.Should().BeTrue(dashboard.Message);
            graphQl.Count.Should().Be(dashboard.Count);
            Normalized(graphQl.Message, graphQl.Id, viaGraphQl)
                .Should()
                .Be(Normalized(dashboard.Message, dashboard.Id, viaDashboard));

            var byGraphQl = (await _host.QueuedResumesOf(viaGraphQl))
                .Should()
                .ContainSingle()
                .Subject;
            var byDashboard = (await _host.QueuedResumesOf(viaDashboard))
                .Should()
                .ContainSingle()
                .Subject;
            byGraphQl.Id.Should().Be(graphQl.Id!.Value);
            byDashboard.Id.Should().Be(dashboard.Id!.Value);
            byGraphQl.ResumeAt.Should().Be(from, $"it resumes where it was asked ({Adr})");
            new
            {
                byGraphQl.TrainName,
                byGraphQl.Input,
                byGraphQl.ResumeAt,
                byGraphQl.ManifestId,
                byGraphQl.Priority,
            }
                .Should()
                .BeEquivalentTo(
                    new
                    {
                        byDashboard.TrainName,
                        byDashboard.Input,
                        byDashboard.ResumeAt,
                        byDashboard.ManifestId,
                        byDashboard.Priority,
                    },
                    "both surfaces queue the one operation's resume (central ADR 0022)"
                );
            byGraphQl.TrainName.Should().Be(typeof(IResumableTrain).FullName);
            byGraphQl.ManifestId.Should().BeNull("it is queued through the mediator");
        }
    }

    [Test]
    public async Task A_point_no_checkpoint_covers_is_refused_with_one_reason_on_both_surfaces()
    {
        // A step before the checkpoint, of a run that wrote one.
        var checkpointed = await FailedRunAsync();
        var before = await BothRefuse(checkpointed, Fetch);
        before.Should().Contain("No checkpoint").And.Contain(Fetch);

        // After the latest checkpoint, of a run that failed before writing any.
        var early = await FailedRunAsync(failIn: nameof(FetchSources));
        (await BothRefuse(early, null)).Should().Contain("wrote no checkpoint");

        // A step whose input only a skipped step produces.
        (await BothRefuse(checkpointed, "PublishSummary#0"))
            .Should()
            .Contain(nameof(SourceSummary), "the check names the type nothing restores");
    }

    [Test]
    public async Task A_running_pending_or_completed_run_is_refused()
    {
        var completed = await _host.RunAsync<IResumableTrain>(failIn: null);
        var pending = await SeedRunAsync(TrainState.Pending);
        var running = await SeedRunAsync(TrainState.InProgress);

        foreach (
            var (run, state) in new[]
            {
                (completed, TrainState.Completed),
                (pending, TrainState.Pending),
                (running, TrainState.InProgress),
            }
        )
            (await BothRefuse(run, null))
                .Should()
                .Be(
                    $"Execution {run} is {state}; only a failed or cancelled run can be resumed.",
                    $"only a run that ended without finishing has work left to resume ({Adr})"
                );
    }

    [Test]
    public async Task A_second_resume_while_one_is_queued_is_refused()
    {
        var run = await FailedRunAsync();

        var first = Result(await _host.ResumeOverGraphQLAsync(run, null));
        first.Success.Should().BeTrue(first.Message);

        var reason = await BothRefuse(run, Summarize);
        reason
            .Should()
            .Contain($"A resume of execution {run} is already queued")
            .And.Contain($"WorkQueue {first.Id}");
        (await _host.QueuedResumesOf(run))
            .Should()
            .ContainSingle($"one queued resume per run ({Adr})");
    }

    [Test]
    public async Task A_resume_of_an_invoked_run_is_refused_with_its_reason()
    {
        var run = await FailedRunAsync();
        await _host.With(d =>
            d.Metadatas.Where(m => m.Id == run)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.InvokingMachine, "Research.Machine"))
        );

        (await BothRefuse(run, null))
            .Should()
            .Contain("state machine 'Research.Machine'")
            .And.Contain(
                "cannot be resumed",
                "only the machine's step receives the outcome of a run it invoked (central ADR 0046)"
            );
    }

    [TestCase("""{"_truncated":true,"_size":9999999}""", "too large to save in full")]
    [TestCase("""{"_unserializable":true}""", "_unserializable placeholder")]
    [TestCase("""{"Topic":{"_redacted":true}}""", "masked by [TraxSensitive]")]
    public async Task A_placeholder_or_masked_input_is_refused(string saved, string reason)
    {
        var run = await FailedRunAsync();
        await _host.With(d =>
            d.Metadatas.Where(m => m.Id == run)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Input, saved))
        );

        (await BothRefuse(run, null))
            .Should()
            .Contain(reason, "a resume runs with the saved input, checked as a requeue checks it");
    }

    [Test]
    public async Task A_caller_past_the_gate_but_not_the_trains_authorize_is_refused()
    {
        var refused = await FailedRunAsync<IGuardedResumableTrain>();

        var operatorOnly = await _host.ResumeOverGraphQLAsync(
            refused,
            null,
            ResumeHost.OperatorKey
        );

        ErrorCodes(operatorOnly)
            .Should()
            .Contain(
                "TRAX_AUTHORIZATION",
                "a resume enqueues through the mediator, so the train's [TraxAuthorize] applies "
                    + $"past the operations gate ({Adr})"
            );
        operatorOnly
            .RootElement.GetRawText()
            .Should()
            .NotContain("Resumer", "which requirement was missing is the server's to know");
        (await _host.QueuedResumesOf(refused)).Should().BeEmpty("nothing was queued");

        // Nor does a caller who is not past the gate get as far as the train.
        ErrorCodes(await _host.ResumeOverGraphQLAsync(refused, null, apiKey: null))
            .Should()
            .Contain("TRAX_AUTHORIZATION");

        // A caller in the train's role may, and the dashboard, gated as a whole by its host, may.
        var resumer = Result(
            await _host.ResumeOverGraphQLAsync(refused, null, ResumeHost.ResumerKey)
        );
        resumer.Success.Should().BeTrue(resumer.Message);

        var dashboard = await _host.ResumeAsTheDashboardAsync(
            await FailedRunAsync<IGuardedResumableTrain>(),
            null
        );
        dashboard
            .Success.Should()
            .BeTrue(
                dashboard.Message
                    + "; the dashboard resumes in its trusted scope, as it requeues (docs/0017)"
            );
    }

    [Test]
    public async Task No_operator_surface_returns_a_checkpoints_state_or_a_withheld_track()
    {
        var run = await FailedRunAsync();

        // The row holds the state, so a surface that showed it would show the secret.
        var rows = await _host.With(d =>
            d.Checkpoints.AsNoTracking().Where(c => c.MetadataId == run).ToListAsync()
        );
        rows.Should().ContainSingle().Which.State.Should().Contain(ResumeProbe.StateSecret);
        rows[0]
            .Tracks.Should()
            .NotContain(nameof(ResumeVault), "a sensitive route's track is never stored");

        var graphQlGraph = await _host.GraphQLAsync(
            $$"""
            {
              operations {
                runGraph(metadataId: {{run}}) {
                  metadataId train hasGraph hash moreSteps canResume
                  nodes { ...node tracks { name taken nodes { ...node tracks { name taken nodes { ...node } } } } }
                  unmatchedSteps { position name nameWithheld answer answerWithheld }
                }
              }
            }
            fragment node on RunGraphNode {
              id kind junction in out opaque state replayed trackTaken canResume checkpointed
              steps { position kind name state answer answerWithheld nameWithheld failureException }
            }
            """
        );
        var graphQlRun = await _host.GraphQLAsync(
            $$"""
            {
              operations {
                executionDetail(id: {{run}}) { id name input output failureJunction failureException failureReason stackTrace }
                junctionRuns(metadataId: {{run}}) { position name answer answerWithheld nameWithheld }
              }
            }
            """
        );
        graphQlGraph
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(Raw(graphQlGraph));
        graphQlRun.RootElement.TryGetProperty("errors", out _).Should().BeFalse(Raw(graphQlRun));

        // The dashboard's reads: the run graph's, and the resume check behind it.
        var dashboardGraph = await DashboardGraphAsync(run);
        var declared = _host
            .Services.GetRequiredService<ITrainChainGraphs>()
            .FindDeclared(typeof(IResumableTrain).FullName!)!;
        var checks = await _host
            .Services.GetRequiredService<IRunResumes>()
            .CheckMany(
                declared.Train,
                declared.Chain,
                declared.Input,
                declared.Output,
                run,
                [Summarize, Fetch],
                CancellationToken.None
            );

        // And a resume's answer on each surface.
        var resumedOverGraphQl = await _host.ResumeOverGraphQLAsync(run, Summarize);
        var refusedOnDashboard = await _host.ResumeAsTheDashboardAsync(run, null);

        var surfaces = new Dictionary<string, string>
        {
            ["runGraph"] = Raw(graphQlGraph),
            ["execution and junctionRuns"] = Raw(graphQlRun),
            ["resumeExecution"] = Raw(resumedOverGraphQl),
            ["the dashboard's run graph"] = JsonSerializer.Serialize(dashboardGraph),
            ["the resume check"] = JsonSerializer.Serialize(checks),
            ["the dashboard's resume"] = JsonSerializer.Serialize(refusedOnDashboard),
        };
        foreach (var (surface, body) in surfaces)
            body.Should()
                .NotContain(
                    ResumeProbe.StateSecret,
                    $"{surface} says a checkpoint exists, never what it holds ({Adr})"
                );

        // The vault's route: which track was taken is told by neither run graph.
        var vault = Flatten(
                graphQlGraph
                    .RootElement.GetProperty("data")
                    .GetProperty("operations")
                    .GetProperty("runGraph")
                    .GetProperty("nodes")
            )
            .Single(n => n.GetProperty("kind").GetString() == "SWITCH");
        vault.GetProperty("trackTaken").ValueKind.Should().Be(JsonValueKind.Null);
        vault
            .GetProperty("tracks")
            .EnumerateArray()
            .Should()
            .OnlyContain(t => !t.GetProperty("taken").GetBoolean());
        var dashboardVault = dashboardGraph.Nodes.Single(n =>
            n.Kind == Core.Monad.ChainStepKind.Switch
        );
        dashboardVault.TrackTaken.Should().BeNull();
        dashboardVault.Tracks.Should().OnlyContain(t => !t.Taken);
        dashboardVault
            .Tracks.Select(t => t.Nodes.Single().State)
            .Distinct()
            .Should()
            .ContainSingle("both tracks stand alike, so neither gives the answer away");

        // What each surface does say: that the checkpoint exists, and at which node.
        dashboardGraph
            .Nodes.Where(n => n.Checkpointed)
            .Select(n => n.Id)
            .Should()
            .Equal("Checkpoint<FetchedSources>#0");
        resumedOverGraphQl
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("resumeExecution")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(Raw(resumedOverGraphQl));
    }

    /// <summary>
    /// Asks both surfaces to resume <paramref name="run"/> at <paramref name="from"/>, checks
    /// both refuse it with the same reason and queue nothing, and returns the reason.
    /// </summary>
    private async Task<string> BothRefuse(long run, string? from)
    {
        var queuedBefore = (await _host.QueuedResumesOf(run)).Count;

        var graphQl = Result(await _host.ResumeOverGraphQLAsync(run, from));
        var dashboard = await _host.ResumeAsTheDashboardAsync(run, from);

        graphQl.Success.Should().BeFalse("GraphQL refuses it");
        dashboard.Success.Should().BeFalse("the dashboard refuses it");
        graphQl.Id.Should().BeNull();
        graphQl
            .Message.Should()
            .NotBeNullOrWhiteSpace()
            .And.Be(dashboard.Message, "one reason on both surfaces (central ADR 0022)");
        (await _host.QueuedResumesOf(run)).Should().HaveCount(queuedBefore, "nothing was queued");
        return graphQl.Message!;
    }

    private Task<long> FailedRunAsync(string failIn = nameof(SummarizeSources)) =>
        FailedRunAsync<IResumableTrain>(failIn);

    private async Task<long> FailedRunAsync<TTrain>(string failIn = nameof(SummarizeSources))
        where TTrain : Effect.Services.ServiceTrain.IServiceTrain<ResumableInput, string>
    {
        var run = await _host.RunAsync<TTrain>(failIn);
        var state = await _host.With(d =>
            d.Metadatas.AsNoTracking()
                .Where(m => m.Id == run)
                .Select(m => m.TrainState)
                .SingleAsync()
        );
        state.Should().Be(TrainState.Failed);
        return run;
    }

    private async Task<long> SeedRunAsync(TrainState state)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IResumableTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = """{"Topic":"graphs"}""";
        run.TrainState = state;

        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        await db.Track(run);
        await db.SaveChanges(CancellationToken.None);
        return run.Id;
    }

    private async Task<Api.DTOs.RunGraph> DashboardGraphAsync(long run)
    {
        await using var db = await _host
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);
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

    private static OperationResult Result(JsonDocument response)
    {
        response.RootElement.TryGetProperty("errors", out _).Should().BeFalse(Raw(response));
        var result = response
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("resumeExecution");
        return new OperationResult(
            result.GetProperty("success").GetBoolean(),
            Id: result.GetProperty("id") is { ValueKind: JsonValueKind.Number } id
                ? id.GetInt64()
                : null,
            Count: result.GetProperty("count") is { ValueKind: JsonValueKind.Number } count
                ? count.GetInt32()
                : null,
            Message: result.GetProperty("message").GetString()
        );
    }

    // A success message names the entry and the run it resumes, which differ per surface.
    private static string? Normalized(string? message, long? entry, long run) =>
        message is null
            ? null
            : Regex.Replace(
                message
                    .Replace(entry?.ToString() ?? "\0", "{entry}", StringComparison.Ordinal)
                    .Replace(run.ToString(), "{run}", StringComparison.Ordinal),
                @"\s+",
                " "
            );

    private static List<string?> ErrorCodes(JsonDocument response) =>
        response.RootElement.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                        ? c.GetString()
                        : null
                )
                .ToList()
            : [];

    private static IEnumerable<JsonElement> Flatten(JsonElement nodes) =>
        nodes
            .EnumerateArray()
            .SelectMany(n =>
                new[] { n }.Concat(
                    n.TryGetProperty("tracks", out var tracks)
                        ? tracks
                            .EnumerateArray()
                            .SelectMany(t =>
                                t.TryGetProperty("nodes", out var inner) ? Flatten(inner) : []
                            )
                        : []
                )
            );

    private static string Raw(JsonDocument document) => document.RootElement.GetRawText();
}
