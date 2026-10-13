using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Testing;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Trains.StartRun.Junctions;
using Trax.Scheduler.Services.Operations;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The research train stores its checked findings in a checkpoint after the Scale step. A run that
/// crashed writing the report resumes from it, on GraphQL (<c>resumeExecution</c>) and from the
/// dashboard (its Resume button, the same operation in its trusted scope): the resumed run writes
/// the report from the stored findings, and its junction events show it ran nothing before
/// <c>Summarize</c>. See
/// Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.
/// </summary>
[TestFixture]
public class ResearchResumeTests : RecoveryTestFixture
{
    private const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    private const string Checkpoint = "Checkpoint<CheckedFindings>#0";
    private const string Summarize = "Summarize#0";
    private const string FetchFullTexts = "Scale<Depth>#0/CrossCheck/FetchFullTexts#0";

    private static readonly string ConnectionString = TestPostgres.WithPort(
        RecoveryApiFactory.DefaultConnectionString
    );

    [Test]
    public async Task A_crash_in_Summarize_resumes_from_CheckedFindings_without_fetching_again()
    {
        var before = await TraxInvariants.FindViolationsAsync(ConnectionString);

        // Summarize crashes on every attempt, so the manifest's retries resume and fail too, and
        // the failed runs are left for an operator to resume.
        await Run.StartAsync("RESEARCH", crashOnce: true);
        var faults = SharedRecoverySetup.Factory.Services.GetRequiredService<FaultInjector>();
        faults.ArmEveryAttempt(Run.RunId, CrashPoint.Report);

        var failed = await Run.FollowAttemptAsync(1);
        (await Run.WaitForEndAsync(failed)).Should().Be("FAILED");
        var crashed = await Run.TimelineAsync(failed);
        Names(crashed).Should().Contain("FetchFullTexts", "the papers' findings are cross-checked");
        crashed.Last().GetProperty("name").GetString().Should().Be("Summarize");

        // The manifest's retries resume the failed run by themselves: each runs only Summarize.
        var retry = await Run.FollowAttemptAsync(2);
        (await Run.WaitForEndAsync(retry)).Should().Be("FAILED");
        Names(await Run.TimelineAsync(retry)).Should().Equal(["Summarize"]);
        var lastRetry = await Run.FollowAttemptAsync(ScheduleDemoRun.MaxRetries + 1);
        (await Run.WaitForEndAsync(lastRetry)).Should().Be("FAILED");

        faults.Disarm(Run.RunId);

        // What the operator sees on the failed run: a checkpoint after the Scale step, and Summarize
        // offered as a point to resume at; nothing before the checkpoint is.
        var (canResume, nodes) = await Run.RunGraphAsync(failed);
        canResume.Should().BeTrue($"the failed run has a checkpoint to resume after ({Adr})");
        nodes[Checkpoint].GetProperty("checkpointed").GetBoolean().Should().BeTrue();
        nodes[Summarize].GetProperty("canResume").GetBoolean().Should().BeTrue();
        nodes[FetchFullTexts]
            .GetProperty("canResume")
            .GetBoolean()
            .Should()
            .BeFalse("no checkpoint before it holds what it needs");

        // On GraphQL, at the step the page's button names.
        var overGraphQL = await ResumeOverGraphQLAsync(failed, Summarize);
        await ShouldHaveRunOnlySummarize(overGraphQL, null, "resumeExecution on GraphQL");
        var report = await OutputAsync(overGraphQL);

        // From the dashboard, after the latest checkpoint, as its Resume button asks, on the
        // retry, which reads the failed run's checkpoint back through the run it resumed.
        var fromDashboard = await ResumeAsTheDashboardAsync(retry);
        await ShouldHaveRunOnlySummarize(fromDashboard, report, "the dashboard's Resume");

        // A run whose resume completed has no work left: neither surface resumes it again, and
        // its graph no longer offers it.
        (await Run.RunGraphAsync(failed))
            .CanResume.Should()
            .BeFalse($"its resume completed ({Adr})");
        (await RefusedOverGraphQLAsync(failed)).Should().Contain("already completed");

        Decider
            .Asked(Run.RunId, "Source")
            .Should()
            .Be(1, "both questions come before the checkpoint, so no resume asks them");
        Decider.Asked(Run.RunId, "Depth").Should().Be(1);
        Stream.Errors.Should().BeEmpty();

        // Every invariant that holds while the host runs; the first three hold once it stops.
        var transient = new[]
        {
            TraxInvariants.RunInProgress,
            TraxInvariants.EffectClaimInFlight,
            TraxInvariants.DispatchedWithoutRun,
        };
        var added = (await TraxInvariants.FindViolationsAsync(ConnectionString))
            .Except(before)
            .ToList();
        added
            .Where(v => !transient.Contains(v.Invariant))
            .Should()
            .BeEmpty(TraxInvariants.Describe(added));
        var runs = new[] { failed, retry, lastRetry, overGraphQL, fromDashboard }.Select(id =>
            id.ToString()
        );
        added
            .Where(v => v.Invariant == TraxInvariants.RunInProgress)
            .Select(v => v.Id)
            .Should()
            .NotIntersectWith(runs, "every run this test started has ended");
    }

    private async Task<string> RefusedOverGraphQLAsync(long run)
    {
        var response = await GraphQL.SendAsync(
            $$"""
            mutation { operations { resumeExecution(id: {{run}}) { success message id } } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var result = response.GetData("operations", "resumeExecution");
        result.GetProperty("success").GetBoolean().Should().BeFalse();
        return result.GetProperty("message").GetString()!;
    }

    private async Task ShouldHaveRunOnlySummarize(long resumed, string? report, string surface)
    {
        await Stream.SubscribeAsync(resumed);
        (await Run.WaitForEndAsync(resumed)).Should().Be("COMPLETED", surface);

        // Its junction events: Summarize, as the node it was in the failed run, and nothing else.
        var steps = await Run.TimelineAsync(resumed);
        Names(steps)
            .Should()
            .Equal(
                ["Summarize"],
                $"{surface} resumed after the checkpoint and fetched nothing again ({Adr})"
            );
        steps.Single().GetProperty("nodeId").GetString().Should().Be(Summarize);
        Questions(steps).Should().BeEmpty();

        // The report it wrote is the one the other resume wrote: the stored findings came back
        // whole.
        if (report is not null)
            (await OutputAsync(resumed)).Should().Be(report, surface);

        // Its run graph marks what it skipped as restored.
        var (_, nodes) = await Run.RunGraphAsync(resumed);
        foreach (var id in new[] { "PlanResearch#0", "Scale<Depth>#0", Checkpoint })
            nodes[id].GetProperty("state").GetString().Should().Be("RESTORED", $"{surface}: {id}");
        nodes[Summarize].GetProperty("state").GetString().Should().Be("COMPLETED");

        // Restored only on the tracks the failed run took: the ones it passed over are skipped,
        // as they are on the failed run's own graph.
        nodes["Switch<Source>#0/Papers/SearchPapers#0"]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("RESTORED", surface);
        nodes["Switch<Source>#0/Web/SearchWeb#0"]
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("SKIPPED", $"{surface}: the failed run never took the Web track");
    }

    private async Task<long> ResumeOverGraphQLAsync(long failed, string from)
    {
        var response = await GraphQL.SendAsync(
            $$"""
            mutation { operations {
              resumeExecution(id: {{failed}}, from: {{JsonSerializer.Serialize(
                from
            )}}) { success message id }
            } }
            """,
            OperatorKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var result = response.GetData("operations", "resumeExecution");
        result
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(result.GetProperty("message").GetString());
        return await Run.RunOfEntryAsync(result.GetProperty("id").GetInt64());
    }

    private async Task<long> ResumeAsTheDashboardAsync(long failed)
    {
        OperationResult result;
        using (var scope = SharedRecoverySetup.Factory.Services.CreateScope())
        {
            var trusted = scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>();
            using (trusted.BeginTrusted("dashboard"))
                result = await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .ResumeExecutionAsync(failed, null, CancellationToken.None);
        }
        result.Success.Should().BeTrue(result.Message);
        return await Run.RunOfEntryAsync(result.Id!.Value);
    }

    private async Task<string> OutputAsync(long metadataId)
    {
        var detail = await GraphQL.SendAsync(
            $$"""{ operations { executionDetail(id: {{metadataId}}) { output } } }""",
            OperatorKey
        );
        detail.HasErrors.Should().BeFalse(detail.FirstErrorMessage);
        return detail.GetData("operations", "executionDetail", "output").GetString()!;
    }
}
