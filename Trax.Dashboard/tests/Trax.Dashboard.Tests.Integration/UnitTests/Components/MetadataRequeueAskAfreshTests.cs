using System.Reflection;
using AwesomeAssertions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Functional;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The run page's "Re-queue, Ask Afresh" goes through
/// <c>IOperationsService.RequeueExecutionAsync</c> with <c>askAfresh</c> set, the call the API's
/// <c>requeueExecution</c> makes with it, so the new entry carries no replay link; the default
/// "Re-queue" keeps it. Each button shows busy while its call runs.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class MetadataRequeueAskAfreshTests
{
    private const string Plain = "Re-queue";
    private const string Afresh = "Re-queue, Ask Afresh";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [TestCase(false)]
    [TestCase(true)]
    public async Task The_requeue_replays_the_runs_decisions_unless_asked_afresh(bool askAfresh)
    {
        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<IAfreshRequeueTrain, AfreshRequeueTrain>();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddRealOperationsService(new TrainDiscoveryService(trains), _data);
        var metadataId = await SeedRunAsync();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        await Button(page, askAfresh ? Afresh : Plain).ClickAsync(new());

        var navigation = _ctx.Services.GetRequiredService<FakeNavigationManager>();
        page.WaitForAssertion(
            () => navigation.Uri.Should().Contain("trax/data/work-queue/"),
            WaitTimeout
        );
        page.FindAll(".rz-alert").Should().BeEmpty();

        await using var db = await _data.CreateDbContextAsync(default);
        var entry = (await db.WorkQueues.AsNoTracking().ToListAsync())
            .Should()
            .ContainSingle()
            .Subject;
        entry.TrainName.Should().Be(typeof(IAfreshRequeueTrain).FullName);
        entry
            .ReplayDecisionsOf.Should()
            .Be(askAfresh ? null : metadataId, "asking afresh queues the run with no replay link");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task The_clicked_requeue_button_shows_busy_until_the_service_returns(
        bool askAfresh
    )
    {
        var pending = new TaskCompletionSource<OperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _ctx.Services.AddDashboardPageServices(_data);
        var operations = PendingOperations.Create(pending.Task, out var calls);
        _ctx.Services.AddScoped(_ => operations);
        var metadataId = await SeedRunAsync();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        // A busy button shows a spinner in place of its label, so both are found by place.
        var clicked = ButtonIndex(page, askAfresh ? Afresh : Plain);
        var other = ButtonIndex(page, askAfresh ? Plain : Afresh);

        var click = Button(page, askAfresh ? Afresh : Plain).ClickAsync(new());

        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeTrue(), WaitTimeout);
        IsBusy(page, other).Should().BeFalse("only the clicked button is busy");
        page.FindAll("button")
            .ElementAt(other)
            .HasAttribute("disabled")
            .Should()
            .BeTrue("neither re-queues again while one is in flight");
        var call = calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(nameof(IOperationsService.RequeueExecutionAsync));
        call.Args.Any(a => a is true).Should().Be(askAfresh);

        pending.SetResult(new OperationResult(false, Message: "refused"));
        await click;
        page.WaitForAssertion(() => IsBusy(page, clicked).Should().BeFalse(), WaitTimeout);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_second_requeue_click_while_the_first_runs_queues_nothing(bool askAfresh)
    {
        var pending = new TaskCompletionSource<OperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _ctx.Services.AddDashboardPageServices(_data);
        var operations = PendingOperations.Create(pending.Task, out var calls);
        _ctx.Services.AddScoped(_ => operations);
        var metadataId = await SeedRunAsync();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        // Both captured before the first click, as a browser that has not yet applied the render
        // disabling them still holds them: neither Blazor nor the button checks Disabled on the
        // server, so the second event is dispatched.
        var first = Button(page, askAfresh ? Afresh : Plain);
        var second = Button(page, askAfresh ? Plain : Afresh);

        var firstClick = first.ClickAsync(new());
        page.WaitForAssertion(() => calls.Should().ContainSingle(), WaitTimeout);
        // Not awaited: without the guard it would wait on the held service call.
        var secondClick = second.ClickAsync(new());
        var callsWhileHeld = calls.Count;

        pending.SetResult(new OperationResult(false, Message: "refused"));
        await firstClick;
        await secondClick;

        callsWhileHeld
            .Should()
            .Be(1, "a second click while a re-queue is in flight would queue a second run");
        calls.Should().ContainSingle();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task The_requeue_notification_carries_the_services_message(bool askAfresh)
    {
        const string message =
            "Work queue entry 12 created. It asks its deciders afresh: the decisions of "
            + "execution 3 are already replayed by another run or queued entry, and are replayed once.";
        _ctx.Services.AddDashboardPageServices(_data);
        var operations = PendingOperations.Create(
            Task.FromResult(new OperationResult(true, Id: 12, Count: 1, Message: message)),
            out _
        );
        _ctx.Services.AddScoped(_ => operations);
        var metadataId = await SeedRunAsync();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p =>
            p.Add(x => x.MetadataId, metadataId)
        );
        page.WaitForElement("button:contains('Re-queue')", WaitTimeout);
        await Button(page, askAfresh ? Afresh : Plain).ClickAsync(new());

        var notice = _ctx
            .Services.GetRequiredService<NotificationService>()
            .Messages.Should()
            .ContainSingle()
            .Subject;
        notice.Severity.Should().Be(NotificationSeverity.Success);
        notice.Detail.Should().Contain(message, "the operator sees what the API's caller sees");
    }

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

    private async Task<long> SeedRunAsync()
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IAfreshRequeueTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = """{"Value":"again"}""";

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(run);
        await db.SaveChanges(default);

        // The run recorded a decision, so its default re-queue carries the replay link.
        db.RecordedDecisions.Add(
            new RecordedDecision
            {
                MetadataId = run.Id,
                QuestionKey = "Route",
                Occurrence = 0,
                Fingerprint = new string('0', 64),
                Kind = "choice",
                Question = "{}",
                Answer = "\"Express\"",
                Routes = """[{"track": "Express", "fallback_reason": null}]""",
                DecidedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChanges(default);
        return run.Id;
    }

    /// <summary>An operations service whose every call records itself and waits on one task.</summary>
    public class PendingOperations : DispatchProxy
    {
        private object _result = null!;
        private List<(string Method, object?[] Args)> _calls = null!;

        public static IOperationsService Create(
            Task<OperationResult> result,
            out List<(string Method, object?[] Args)> calls
        )
        {
            var proxy = Create<IOperationsService, PendingOperations>();
            var self = (PendingOperations)(object)proxy;
            self._result = result;
            self._calls = calls = [];
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _calls.Add((targetMethod!.Name, args ?? []));
            return _result;
        }
    }

    public record AfreshRequeueInput
    {
        public string Value { get; init; } = "";
    }

    public interface IAfreshRequeueTrain : IServiceTrain<AfreshRequeueInput, Unit> { }

    public class AfreshRequeueTrain : ServiceTrain<AfreshRequeueInput, Unit>, IAfreshRequeueTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
