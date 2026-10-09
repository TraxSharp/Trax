using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Exceptions;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Dashboard.Tests.Integration.Fakes.Trains;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.Checkpoints;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The run page's Resume button, beside Re-queue: shown when the run graph says the run can
/// resume after its latest checkpoint, it calls <c>IOperationsService.ResumeExecutionAsync</c> with
/// no step, the call the API's <c>resumeExecution</c> makes without <c>from</c>, inside the
/// dashboard's trusted scope; it navigates to the queued entry, or shows the operation's reason
/// when it refuses.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[Property(
    "adr",
    "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md"
)]
public class MetadataResumeTests
{
    private const string Resume = "Resume";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private ScriptedRunResumes _resumes = null!;
    private List<(string Method, bool Trusted)> _calls = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _resumes = new ScriptedRunResumes { AllowsLatest = true };
        _resumes.AllowedAt.Add(ResumeGraphTrain.Summarize);
        _resumes.Checkpoints.Add(ResumeGraphTrain.Checkpoint);
        _calls = [];
        ResumablePage.AddServices(_ctx.Services, _data, _resumes, _calls);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Resume_queues_a_resume_after_the_latest_checkpoint_through_the_shared_operation()
    {
        var runId = await ResumablePage.SeedFailedRunAsync(_data);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        await ResumeButton(page).ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        page.WaitForAssertion(
            () => navigation.Uri.Should().Contain("trax/data/work-queue/"),
            WaitTimeout
        );
        page.FindAll(".rz-alert").Should().BeEmpty();
        _calls
            .Should()
            .Equal(
                [(nameof(IOperationsService.ResumeExecutionAsync), true)],
                "the page makes the API's resumeExecution call, inside the dashboard's trusted "
                    + "scope (docs/0017), and nothing else"
            );

        await using var db = await _data.CreateDbContextAsync(default);
        var entry = (await db.WorkQueues.AsNoTracking().ToListAsync())
            .Should()
            .ContainSingle()
            .Subject;
        entry.ResumeFrom.Should().Be(runId);
        entry.ResumeAt.Should().BeNull("Resume resumes after the run's latest checkpoint");
        navigation.Uri.Should().EndWith($"trax/data/work-queue/{entry.Id}");
        _ctx.Services.GetRequiredService<NotificationService>()
            .Messages.Should()
            .ContainSingle()
            .Which.Detail.Should()
            .Contain("queued to resume");
    }

    [Test]
    public async Task A_refused_resume_shows_the_operations_reason_and_queues_nothing()
    {
        var runId = await ResumablePage.SeedFailedRunAsync(_data);
        // A resume of the run is already queued, which the operation refuses and the graph cannot
        // know.
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            var queued = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = typeof(IResumeGraphTrain).FullName!,
                    Input = """{"Topic":"graphs"}""",
                    InputTypeName = typeof(ResumeGraphInput).FullName,
                }
            );
            queued.ResumeFrom = runId;
            await db.Track(queued);
            await db.SaveChanges(default);
        }

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        await ResumeButton(page).ClickAsync(new());

        page.WaitForAssertion(
            () =>
                page.Find(".rz-alert")
                    .TextContent.Should()
                    .Contain($"A resume of execution {runId} is already queued"),
            WaitTimeout
        );
        _ctx.Services.GetRequiredService<FakeNavigationManager>()
            .Uri.Should()
            .NotContain("work-queue");
        await using var check = await _data.CreateDbContextAsync(default);
        (await check.WorkQueues.CountAsync()).Should().Be(1, "nothing more was queued");
    }

    [Test]
    public async Task A_run_that_cannot_resume_shows_no_Resume_button()
    {
        _resumes.AllowsLatest = false;
        _resumes.AllowedAt.Clear();
        var runId = await ResumablePage.SeedFailedRunAsync(_data);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        page.WaitForElement("li.cs-rg-node", WaitTimeout);
        Labels(page).Should().NotContain(Resume, "nothing the run stored lets it resume");
        page.FindAll(".cs-rg-resume").Should().BeEmpty();
    }

    [Test]
    public async Task A_completed_run_shows_no_Resume_button_whatever_the_check_says()
    {
        var runId = await ResumablePage.SeedFailedRunAsync(_data, TrainState.Completed);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        page.WaitForElement("li.cs-rg-node", WaitTimeout);
        Labels(page).Should().NotContain(Resume);
        page.FindAll(".cs-rg-resume").Should().BeEmpty();
    }

    private static AngleSharp.Dom.IElement ResumeButton(IRenderedComponent<MetadataDetailPage> page)
    {
        page.WaitForAssertion(() => Labels(page).Should().Contain(Resume), WaitTimeout);
        return page.FindAll("button")
            .First(b => b.QuerySelector(".rz-button-text")?.TextContent.Trim() == Resume);
    }

    private static List<string?> Labels(IRenderedFragment page) =>
        page.FindAll("button")
            .Select(b => b.QuerySelector(".rz-button-text")?.TextContent.Trim())
            .ToList();
}

/// <summary>
/// The run page over a host that registers <see cref="ResumeGraphTrain"/> with its declared chain,
/// a scripted resume check, and the real operations service, recording each call it takes and
/// whether it came inside a trusted scope.
/// </summary>
internal static class ResumablePage
{
    public static void AddServices(
        IServiceCollection services,
        InMemoryDataContextFactory data,
        IRunResumes resumes,
        List<(string Method, bool Trusted)> calls
    )
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IResumeGraphTrain, ResumeGraphTrain>();
        var graphs = new FixedChainGraphs().WithDeclared<
            ResumeGraphTrain,
            ResumeGraphInput,
            string
        >(typeof(IResumeGraphTrain).FullName!);

        services.AddSingleton<Trax.Mediator.Services.ChainVerification.ITrainChainGraphs>(graphs);
        services.AddDashboardPageServices(data);
        services.AddRealOperationsService(new TrainDiscoveryService(trains), data);
        services.AddSingleton(UnusedService<ITraxScheduler>.Create());
        services.AddSingleton(resumes);
        services.AddScoped<IOperationsService>(sp =>
            MetadataRequeueRefusalTests.RecordingOperations.Wrap(
                new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    data,
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>(),
                    sp
                ),
                sp.GetRequiredService<ITrustedExecutionScope>(),
                calls
            )
        );
    }

    /// <summary>A run of the train that failed in its summary, after writing its checkpoint.</summary>
    public static async Task<long> SeedFailedRunAsync(
        InMemoryDataContextFactory data,
        TrainState state = TrainState.Failed
    )
    {
        await using var db = await data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IResumeGraphTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = """{"Topic":"graphs"}""";
        run.TrainState = state;
        run.EndTime = DateTime.UtcNow;
        await db.Track(run);
        await db.SaveChanges(default);

        db.JunctionRuns.Add(
            Row(run.Id, 0, "FetchPages", ResumeGraphTrain.Fetch, JunctionRunState.Completed)
        );
        db.JunctionRuns.Add(
            Row(
                run.Id,
                1,
                "SummarizePages",
                ResumeGraphTrain.Summarize,
                state == TrainState.Failed ? JunctionRunState.Failed : JunctionRunState.Completed
            )
        );
        await db.SaveChanges(default);
        return run.Id;
    }

    private static JunctionRun Row(
        long runId,
        int position,
        string name,
        string nodeId,
        JunctionRunState state
    ) =>
        new()
        {
            MetadataId = runId,
            Position = position,
            Kind = JunctionRunKind.Junction,
            Name = name,
            State = state,
            StartedAt = DateTime.UtcNow,
            EndedAt = DateTime.UtcNow,
            NodeId = nodeId,
            FailureClass = state == JunctionRunState.Failed ? FailureClass.Permanent : null,
            FailureException = state == JunctionRunState.Failed ? nameof(TimeoutException) : null,
        };
}
