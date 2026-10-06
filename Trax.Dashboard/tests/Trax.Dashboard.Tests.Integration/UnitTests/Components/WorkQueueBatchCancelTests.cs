using System.Reflection;
using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The work queue page's "Cancel Selected" goes through
/// <see cref="IOperationsService.CancelWorkQueueEntriesAsync"/>, the call the API's
/// <c>cancelWorkQueueEntries</c> makes, and reports what the service answered.
/// Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md states the
/// principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class WorkQueueBatchCancelTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Cancel_selected_cancels_what_a_per_entry_cancel_would_and_reports_the_count()
    {
        var queued = await SeedAsync("queued", WorkQueueStatus.Queued);
        var dispatched = await SeedAsync("dispatched", WorkQueueStatus.Dispatched);
        var cancelled = await SeedAsync("cancelled", WorkQueueStatus.Cancelled);

        // What the service's per-entry cancel says about each state, on a second copy of the
        // entries, so the batch is compared with it rather than with a rule restated here.
        var operations = _ctx.Services.GetRequiredService<IOperationsService>();
        var perEntry = new Dictionary<WorkQueueStatus, bool>();
        foreach (
            var status in new[]
            {
                WorkQueueStatus.Queued,
                WorkQueueStatus.Dispatched,
                WorkQueueStatus.Cancelled,
            }
        )
        {
            var twin = await SeedAsync($"twin-{status}", status);
            perEntry[status] = (await operations.CancelWorkQueueEntryAsync(twin, default)).Success;
        }

        var page = _ctx.RenderComponent<WorkQueuePage>();
        foreach (var subject in new[] { "queued", "dispatched", "cancelled" })
        {
            WaitForRow(page, subject);
            await ToggleRow(page, subject);
        }

        await ClickButton(page, "Cancel Selected");

        page.WaitForAssertion(
            () =>
                Messages()
                    .Should()
                    .Contain(
                        "1 of 3 work queue entry(s) cancelled.",
                        "the page shows the service's count"
                    ),
            WaitTimeout
        );
        (await StatusAsync(queued)).Should().Be(WorkQueueStatus.Cancelled);
        (await StatusAsync(dispatched))
            .Should()
            .Be(
                perEntry[WorkQueueStatus.Dispatched]
                    ? WorkQueueStatus.Cancelled
                    : WorkQueueStatus.Dispatched,
                "the batch cancels a dispatched entry only if the per-entry cancel would"
            );
        (await StatusAsync(cancelled)).Should().Be(WorkQueueStatus.Cancelled);
        perEntry[WorkQueueStatus.Queued].Should().BeTrue("the premise: a queued entry cancels");
        ButtonText(page, "Cancel Selected")
            .Should()
            .BeEmpty("an accepted batch clears the selection");
    }

    [Test]
    public async Task A_refused_batch_shows_the_service_message_and_keeps_the_selection()
    {
        _ctx.Services.AddScoped<IOperationsService>(_ => RefusingOperations.Create());
        await SeedAsync("queued", WorkQueueStatus.Queued);

        var page = _ctx.RenderComponent<WorkQueuePage>();
        WaitForRow(page, "queued");
        await ToggleRow(page, "queued");

        await ClickButton(page, "Cancel Selected");

        page.WaitForAssertion(
            () => page.Find(".rz-alert").TextContent.Should().Contain(RefusingOperations.Refusal),
            WaitTimeout
        );
        ButtonText(page, "Cancel Selected")
            .Should()
            .Contain("(1)", "a refused batch leaves the selection for the operator to change");
    }

    private IEnumerable<string> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages.Select(m => m.Detail);

    private async Task<long> SeedAsync(string subject, WorkQueueStatus status)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = subject });
        entry.Status = status;
        db.WorkQueues.Add(entry);
        await db.SaveChanges(default);
        return entry.Id;
    }

    private async Task<WorkQueueStatus> StatusAsync(long id)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (await db.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == id)).Status;
    }

    /// <summary>
    /// Refuses every batch the way the service refuses one over its size limit.
    /// </summary>
    public class RefusingOperations : DispatchProxy
    {
        public const string Refusal = "At most 1000 ids can be given at once; 1001 were.";

        public static IOperationsService Create() =>
            Create<IOperationsService, RefusingOperations>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(Task<OperationResult>)
                ? Task.FromResult(new OperationResult(false, Count: 0, Message: Refusal))
                : throw new InvalidOperationException(
                    $"IOperationsService.{targetMethod?.Name} was not expected."
                );
    }
}
