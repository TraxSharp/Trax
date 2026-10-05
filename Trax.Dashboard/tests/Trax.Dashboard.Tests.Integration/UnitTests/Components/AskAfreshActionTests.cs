using System.Reflection;
using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
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
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The trigger and dead-letter re-queue actions offer "ask afresh" beside their default, and only
/// that choice calls the scheduler's <c>askAfresh</c> overloads, which the API's mutations call
/// with <c>askAfresh</c> set; the default actions keep their own overloads. A manifest's "replay
/// decisions on retry" is set through <c>IOperationsService.SetManifestsReplayDecisionsOnRetryAsync</c>,
/// from its page and in bulk from the manifests list.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class AskAfreshActionTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingScheduler _scheduler = null!;

    // What an askAfresh trigger reports as the run its entry still replays.
    private long? TriggerReplays { get; set; }

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        var (scheduler, recorder) = RecordingScheduler.Create();
        recorder.Respond = (method, args) =>
            method switch
            {
                // The askAfresh overload says what the trigger did; the default returns nothing.
                nameof(ITraxScheduler.TriggerAsync) => args.Any(a => a is true)
                    ? Task.FromResult(TriggerResult(replayDecisionsOf: TriggerReplays))
                    : Task.CompletedTask,
                nameof(ITraxScheduler.RequeueDeadLetterAsync) => Task.FromResult(
                    new DeadLetterOperationResult(true, 99, "re-queued")
                ),
                nameof(ITraxScheduler.RequeueDeadLettersAsync) => Task.FromResult(
                    new BatchDeadLetterResult(((long[])args[0]!).Length, "re-queued")
                ),
                nameof(ITraxScheduler.RequeueAllDeadLettersAsync) => Task.FromResult(
                    new BatchDeadLetterResult(1, "re-queued all")
                ),
                _ => throw new InvalidOperationException($"{method} was not expected."),
            };
        _scheduler = recorder;
        _ctx.Services.AddSingleton(scheduler);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [TestCase(false)]
    [TestCase(true)]
    public async Task Run_now_on_the_manifest_page_asks_afresh_only_when_chosen(bool askAfresh)
    {
        var (manifestId, _) = await SeedManifestAsync("run-now", "ext-run-now");

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );
        page.WaitForElement("button:contains('Run Now')", WaitTimeout);
        await Click(page, askAfresh ? "Run Now, Ask Afresh" : "Run Now");

        var args = _scheduler.CallsTo(nameof(ITraxScheduler.TriggerAsync)).Single();
        args[0].Should().Be("ext-run-now");
        AskedAfresh(args).Should().Be(askAfresh);
    }

    [TestCase(null, NotificationSeverity.Success, "has been queued for execution")]
    [TestCase(41L, NotificationSeverity.Warning, "replays the decisions of run 41")]
    public async Task Run_now_ask_afresh_warns_when_the_run_still_replays(
        long? replays,
        NotificationSeverity severity,
        string detail
    )
    {
        // The dispatcher claimed the queued retry before the trigger reached it, so the trigger
        // could not clear its replay link.
        TriggerReplays = replays;
        var (manifestId, _) = await SeedManifestAsync("still-replays", "ext-still-replays");

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );
        page.WaitForElement("button:contains('Run Now')", WaitTimeout);
        await Click(page, "Run Now, Ask Afresh");

        var notice = _ctx
            .Services.GetRequiredService<NotificationService>()
            .Messages.Should()
            .ContainSingle()
            .Subject;
        notice.Severity.Should().Be(severity);
        notice.Detail.Should().Contain(detail);
    }

    [Test]
    public async Task Trigger_selected_ask_afresh_reports_a_manifest_that_still_replays()
    {
        TriggerReplays = 41;
        await SeedManifestAsync("group-a", "ext-a");

        var page = _ctx.RenderComponent<ManifestsPage>();
        WaitForRow(page, "group-a");
        await ToggleRow(page, "group-a");
        await Click(page, "Trigger Selected, Ask Afresh (1)");

        page.WaitForAssertion(
            () =>
                _ctx
                    .Services.GetRequiredService<NotificationService>()
                    .Messages.Should()
                    .ContainSingle(m =>
                        m.Severity == NotificationSeverity.Warning
                        && m.Detail == "0 queued, 1 already dispatched and still replaying."
                    ),
            WaitTimeout
        );
        page.Markup.Should().Contain("replays the decisions of run 41 rather than asking afresh");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Trigger_selected_asks_afresh_only_when_chosen(bool askAfresh)
    {
        await SeedManifestAsync("group-a", "ext-a");

        var page = _ctx.RenderComponent<ManifestsPage>();
        WaitForRow(page, "group-a");
        await ToggleRow(page, "group-a");
        await Click(page, askAfresh ? "Trigger Selected, Ask Afresh (1)" : "Trigger Selected (1)");

        page.WaitForAssertion(
            () => _scheduler.CallsTo(nameof(ITraxScheduler.TriggerAsync)).Should().ContainSingle(),
            WaitTimeout
        );
        AskedAfresh(_scheduler.CallsTo(nameof(ITraxScheduler.TriggerAsync)).Single())
            .Should()
            .Be(askAfresh);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Requeue_all_dead_letters_asks_afresh_only_when_chosen(bool askAfresh)
    {
        await SeedDeadLetterAsync();

        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");
        await Click(page, askAfresh ? "Requeue All, Ask Afresh" : "Requeue All");

        page.WaitForAssertion(
            () =>
                _scheduler
                    .CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync))
                    .Should()
                    .ContainSingle(),
            WaitTimeout
        );
        AskedAfresh(_scheduler.CallsTo(nameof(ITraxScheduler.RequeueAllDeadLettersAsync)).Single())
            .Should()
            .Be(askAfresh);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Requeue_selected_dead_letters_asks_afresh_only_when_chosen(bool askAfresh)
    {
        var id = await SeedDeadLetterAsync();

        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "dead-reason");
        await ToggleRow(page, "dead-reason");
        await Click(page, askAfresh ? "Requeue Selected, Ask Afresh (1)" : "Requeue Selected (1)");

        page.WaitForAssertion(
            () =>
                _scheduler
                    .CallsTo(nameof(ITraxScheduler.RequeueDeadLettersAsync))
                    .Should()
                    .ContainSingle(),
            WaitTimeout
        );
        var args = _scheduler.CallsTo(nameof(ITraxScheduler.RequeueDeadLettersAsync)).Single();
        ((long[])args[0]!).Should().Equal(id);
        AskedAfresh(args).Should().Be(askAfresh);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Requeue_on_the_dead_letter_page_asks_afresh_only_when_chosen(bool askAfresh)
    {
        var id = await SeedDeadLetterAsync();

        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p => p.Add(x => x.DeadLetterId, id));
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        await Click(page, askAfresh ? "Re-queue, Ask Afresh" : "Re-queue");

        var args = _scheduler.CallsTo(nameof(ITraxScheduler.RequeueDeadLetterAsync)).Single();
        args[0].Should().Be(id);
        AskedAfresh(args).Should().Be(askAfresh);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Run_now_shows_the_clicked_button_busy_until_the_trigger_returns(
        bool askAfresh
    )
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scheduler.Respond = (_, args) =>
            args.Any(a => a is true)
                ? pending.Task.ContinueWith(_ => TriggerResult(replayDecisionsOf: null))
                : pending.Task;
        var (manifestId, _) = await SeedManifestAsync("busy", "ext-busy");

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );
        page.WaitForElement("button:contains('Run Now')", WaitTimeout);
        const string plain = "Run Now";
        const string afresh = "Run Now, Ask Afresh";

        // A busy button shows a spinner in place of its label, so both are found by place.
        var clicked = ButtonIndex(page, askAfresh ? afresh : plain);
        var other = ButtonIndex(page, askAfresh ? plain : afresh);

        var click = Click(page, askAfresh ? afresh : plain);

        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeTrue(), WaitTimeout);
        IsBusy(page, other).Should().BeFalse("only the clicked button is busy");
        page.FindAll("button")
            .ElementAt(other)
            .HasAttribute("disabled")
            .Should()
            .BeTrue("neither runs again while one is in flight");

        pending.SetResult();
        await click;
        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeFalse(), WaitTimeout);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Requeue_on_the_dead_letter_page_shows_the_clicked_button_busy(bool askAfresh)
    {
        var pending = new TaskCompletionSource<DeadLetterOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _scheduler.Respond = (_, _) => pending.Task;
        var id = await SeedDeadLetterAsync();

        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p => p.Add(x => x.DeadLetterId, id));
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        const string plain = "Re-queue";
        const string afresh = "Re-queue, Ask Afresh";

        // A busy button shows a spinner in place of its label, so both are found by place.
        var clicked = ButtonIndex(page, askAfresh ? afresh : plain);
        var other = ButtonIndex(page, askAfresh ? plain : afresh);

        var click = Click(page, askAfresh ? afresh : plain);

        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeTrue(), WaitTimeout);
        IsBusy(page, other).Should().BeFalse("only the clicked button is busy");
        page.FindAll("button")
            .ElementAt(other)
            .HasAttribute("disabled")
            .Should()
            .BeTrue("neither re-queues again while one is in flight");

        pending.SetResult(new DeadLetterOperationResult(false, null, "refused"));
        await click;
        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeFalse(), WaitTimeout);
    }

    [Test]
    public async Task The_manifest_toggle_sets_replay_on_retry_and_shows_the_new_value()
    {
        var (manifestId, _) = await SeedManifestAsync("toggle", "ext-toggle");

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );
        page.WaitForAssertion(
            () => page.Find(".cs-replay-value").TextContent.Should().Be("Yes"),
            WaitTimeout
        );

        await page.Find(".cs-replay-toggle").ClickAsync(new());

        page.WaitForAssertion(
            () => page.Find(".cs-replay-value").TextContent.Should().Be("No (retries ask afresh)"),
            WaitTimeout
        );
        (await ReplayFlagAsync(manifestId)).Should().BeFalse();
        page.FindAll(".rz-alert").Should().BeEmpty();

        await page.Find(".cs-replay-toggle").ClickAsync(new());

        page.WaitForAssertion(
            () => page.Find(".cs-replay-value").TextContent.Should().Be("Yes"),
            WaitTimeout
        );
        (await ReplayFlagAsync(manifestId)).Should().BeTrue();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_refused_or_failed_toggle_puts_the_switch_back(bool throws)
    {
        _ctx.Services.AddScoped<IOperationsService>(sp =>
            ReplayWriteFails.Wrap(
                new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    _data,
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>(),
                    sp
                ),
                throws
            )
        );
        var (manifestId, _) = await SeedManifestAsync("refused", "ext-refused");

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );
        page.WaitForAssertion(
            () => page.Find(".cs-replay-toggle").ClassList.Should().Contain("rz-switch-checked"),
            WaitTimeout
        );

        await page.Find(".cs-replay-toggle").ClickAsync(new());

        page.WaitForAssertion(
            () => page.Find(".rz-alert").TextContent.Should().Contain(ReplayWriteFails.Message),
            WaitTimeout
        );
        page.Find(".cs-replay-toggle")
            .ClassList.Should()
            .Contain(
                "rz-switch-checked",
                "the switch shows what is stored, not the change that was not saved"
            );
        page.Find(".cs-replay-value").TextContent.Should().Be("Yes");
        (await ReplayFlagAsync(manifestId)).Should().BeTrue();
    }

    [TestCase(false, "Ask Afresh On Retry (2)", "2 of 2 manifest(s) set to ask afresh on retry.")]
    [TestCase(true, "Replay On Retry (2)", "0 of 2 manifest(s) set to replay decisions on retry.")]
    public async Task Bulk_replay_on_retry_goes_through_the_operations_service(
        bool replay,
        string button,
        string message
    )
    {
        var (a, _) = await SeedManifestAsync("group-a", "ext-a");
        var (b, _) = await SeedManifestAsync("group-b", "ext-b");
        var (untouched, _) = await SeedManifestAsync("group-c", "ext-c");

        var page = _ctx.RenderComponent<ManifestsPage>();
        foreach (var group in new[] { "group-a", "group-b" })
        {
            WaitForRow(page, group);
            await ToggleRow(page, group);
        }
        await Click(page, button);

        page.WaitForAssertion(() => Messages().Should().Contain(message), WaitTimeout);
        (await ReplayFlagAsync(a)).Should().Be(replay);
        (await ReplayFlagAsync(b)).Should().Be(replay);
        (await ReplayFlagAsync(untouched)).Should().BeTrue("it was not selected");
    }

    // The askAfresh overloads take the flag right after the id; the default ones take only the
    // cancellation token there, or nothing.
    private static bool AskedAfresh(object?[] args) => args.Any(a => a is true);

    // By the button's label alone, without its icon's ligature text.
    private static Task Click(IRenderedFragment page, string label) =>
        Button(page, label).ClickAsync(new());

    private static AngleSharp.Dom.IElement Button(IRenderedFragment page, string label) =>
        page.FindAll("button")
            .First(b => b.QuerySelector(".rz-button-text")?.TextContent.Trim() == label);

    private static int ButtonIndex(IRenderedFragment page, string label) =>
        page.FindAll("button")
            .ToList()
            .FindIndex(b => b.QuerySelector(".rz-button-text")?.TextContent.Trim() == label);

    // A busy Radzen button shows a spinning icon and no label.
    private static bool IsBusy(IRenderedFragment page, int index)
    {
        var button = page.FindAll("button").ElementAt(index);
        return button.QuerySelector(".rz-button-text") is null
            && button.QuerySelector("i[style*='rotation']") is not null;
    }

    private static ManifestTriggerResult TriggerResult(long? replayDecisionsOf) =>
        new(
            7,
            Created: false,
            null,
            AlreadyDispatched: replayDecisionsOf is not null,
            replayDecisionsOf
        );

    private IEnumerable<string> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages.Select(m => m.Detail);

    private async Task<bool> ReplayFlagAsync(long manifestId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (
            await db.Manifests.AsNoTracking().SingleAsync(m => m.Id == manifestId)
        ).ReplayDecisionsOnRetry;
    }

    private async Task<(long ManifestId, long GroupId)> SeedManifestAsync(
        string groupName,
        string externalId
    )
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = groupName };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IAfreshTrain) });
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return (manifest.Id, group.Id);
    }

    private async Task<long> SeedDeadLetterAsync()
    {
        var (manifestId, _) = await SeedManifestAsync("dead-group", "ext-dead");
        await using var db = await _data.CreateDbContextAsync(default);
        var manifest = await db.Manifests.SingleAsync(m => m.Id == manifestId);
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "dead-reason",
                RetryCount = 3,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    public interface IAfreshTrain { }

    /// <summary>
    /// The real operations service, except that setting replay on retry is refused, or throws.
    /// </summary>
    public class ReplayWriteFails : DispatchProxy
    {
        public const string Message = "replay write refused";

        private IOperationsService _inner = null!;
        private bool _throws;

        public static IOperationsService Wrap(IOperationsService inner, bool throws)
        {
            var proxy = Create<IOperationsService, ReplayWriteFails>();
            var self = (ReplayWriteFails)(object)proxy;
            self._inner = inner;
            self._throws = throws;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (
                targetMethod!.Name
                != nameof(IOperationsService.SetManifestsReplayDecisionsOnRetryAsync)
            )
                return targetMethod.Invoke(_inner, args);

            return _throws
                ? Task.FromException<OperationResult>(new InvalidOperationException(Message))
                : Task.FromResult(new OperationResult(false, Message: Message));
        }
    }
}
