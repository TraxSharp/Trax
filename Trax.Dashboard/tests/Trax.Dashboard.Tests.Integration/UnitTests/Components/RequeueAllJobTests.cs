using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The dead letters page's Requeue All starts the scheduler's background requeue-all job, the one
/// the API's <c>requeueAllDeadLetters</c> starts, and follows it instead of folding the backlog in
/// the operator's circuit: the click returns while the fold runs, the page shows the running job,
/// reports how it ended, and when a requeue-all is already running on this node it starts nothing
/// and follows that one, as an API client reads it back. Enforces
/// <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class RequeueAllJobTests
{
    private const string Adr =
        "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingScheduler _scheduler = null!;
    private TaskCompletionSource<BatchDeadLetterResult> _fold = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _fold = new TaskCompletionSource<BatchDeadLetterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var (scheduler, recorder) = RecordingScheduler.Create();
        recorder.Respond = (method, _) =>
            method == nameof(ITraxScheduler.RequeueAllDeadLettersAsync)
                ? _fold.Task
                : throw new InvalidOperationException($"{method} was not expected.");
        _scheduler = recorder;
        _ctx.Services.AddSingleton(scheduler);
    }

    [TearDown]
    public void TearDown()
    {
        _fold.TrySetCanceled();
        _ctx.Dispose();
    }

    private IDeadLetterRequeueJobs Jobs =>
        _ctx.Services.GetRequiredService<IDeadLetterRequeueJobs>();

    [Test]
    public async Task Requeue_all_returns_while_the_fold_runs_and_reports_how_it_ended()
    {
        await SeedDeadLetterAsync();
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");

        await Click(page, "Requeue All");

        page.WaitForAssertion(
            () =>
                page.Find(".cs-requeue-all-progress")
                    .TextContent.Should()
                    .Contain("Requeueing 1 dead letter(s) awaiting intervention."),
            WaitTimeout
        );
        _fold
            .Task.IsCompleted.Should()
            .BeFalse($"the click returned before the fold ended ({Adr})");
        Button(page, "Requeue All").HasAttribute("disabled").Should().BeTrue();
        Button(page, "Requeue All, Ask Afresh").HasAttribute("disabled").Should().BeTrue();
        Notices().Should().Contain(m => m.Summary == "Requeue All Started");

        _fold.SetResult(new BatchDeadLetterResult(1, "1 dead letter(s) requeued."));

        page.WaitForAssertion(
            () =>
                Notices()
                    .Should()
                    .Contain(m =>
                        m.Severity == NotificationSeverity.Success
                        && m.Detail == "1 dead letter(s) requeued."
                    ),
            WaitTimeout
        );
        page.WaitForAssertion(
            () => page.FindAll(".cs-requeue-all-progress").Should().BeEmpty(),
            WaitTimeout
        );
        Button(page, "Requeue All").HasAttribute("disabled").Should().BeFalse();
        _scheduler
            .CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync))
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task Requeue_all_shows_how_far_the_fold_has_got()
    {
        await SeedDeadLetterAsync();
        await SeedDeadLetterAsync();
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");

        await Click(page, "Requeue All");
        page.WaitForAssertion(
            () => page.Find(".cs-requeue-all-progress").Should().NotBeNull(),
            WaitTimeout
        );
        var progress =
            (IProgress<int>)
                _scheduler.CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync)).Single()[1]!;

        progress.Report(1);

        page.WaitForAssertion(
            () =>
            {
                var shown = page.Find(".cs-requeue-all-progress");
                shown
                    .TextContent.Should()
                    .Contain("Requeued 1 of 2 dead letter(s) awaiting intervention so far.");
                shown
                    .QuerySelector("[aria-valuenow]")!
                    .GetAttribute("aria-valuenow")
                    .Should()
                    .Be("50");
            },
            WaitTimeout
        );
        _fold.SetResult(new BatchDeadLetterResult(2, "2 dead letter(s) requeued."));
    }

    [Test]
    public async Task Requeue_all_while_one_runs_follows_that_one_and_starts_no_other()
    {
        await SeedDeadLetterAsync();
        var running = await Jobs.StartAsync();
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");

        await Click(page, "Requeue All");

        page.WaitForAssertion(
            () =>
                Notices()
                    .Should()
                    .Contain(m =>
                        m.Severity == NotificationSeverity.Warning
                        && m.Summary == "Requeue All Already Running"
                        && m.Detail == running.Message
                    ),
            WaitTimeout
        );
        page.Find(".cs-requeue-all-progress").TextContent.Should().Contain(running.Message);

        _fold.SetResult(new BatchDeadLetterResult(1, "1 dead letter(s) requeued."));

        page.WaitForAssertion(
            () => Notices().Should().Contain(m => m.Detail == "1 dead letter(s) requeued."),
            WaitTimeout
        );
        _scheduler
            .CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync))
            .Should()
            .ContainSingle($"one fold runs per node ({Adr})");
    }

    [Test]
    public async Task Requeue_all_asking_afresh_while_a_replaying_one_runs_says_it_was_not_started()
    {
        await SeedDeadLetterAsync();
        await Jobs.StartAsync(askAfresh: false);
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");

        await Click(page, "Requeue All, Ask Afresh");

        page.WaitForAssertion(
            () =>
                Notices()
                    .Should()
                    .Contain(m =>
                        m.Severity == NotificationSeverity.Warning
                        && m.Detail
                            == DeadLetterRequeueJobs.OtherModeMessage(runningAsksAfresh: false)
                    ),
            WaitTimeout
        );
        _scheduler
            .CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync))
            .Should()
            .ContainSingle();
    }

    [Test]
    public async Task A_failed_requeue_all_says_so_and_nothing_about_the_server()
    {
        await SeedDeadLetterAsync();
        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");

        await Click(page, "Requeue All");
        _fold.SetException(new InvalidOperationException("connection to 10.0.0.5 refused"));

        page.WaitForAssertion(
            () =>
                page.Find(".rz-alert")
                    .TextContent.Should()
                    .Contain(DeadLetterRequeueJobs.FailedMessage),
            WaitTimeout
        );
        page.Markup.Should().NotContain("10.0.0.5");
        page.FindAll(".cs-requeue-all-progress").Should().BeEmpty();
    }

    private IEnumerable<NotificationMessage> Notices() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages;

    // By the button's label alone, without its icon's ligature text.
    private static Task Click(IRenderedFragment page, string label) =>
        Button(page, label).ClickAsync(new());

    private static AngleSharp.Dom.IElement Button(IRenderedFragment page, string label) =>
        page.FindAll("button")
            .First(b => b.QuerySelector(".rz-button-text")?.TextContent.Trim() == label);

    private async Task SeedDeadLetterAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = "dead-group" };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IRequeuedTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);

        await db.Track(
            DeadLetter.Create(
                new CreateDeadLetter
                {
                    Manifest = manifest,
                    Reason = "dead-reason",
                    RetryCount = 3,
                }
            )
        );
        await db.SaveChanges(default);
    }

    public interface IRequeuedTrain { }
}
