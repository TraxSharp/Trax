using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The dashboard cancels runs through <c>IOperationsService.CancelExecutionsAsync</c>, the call
/// the API's <c>cancelExecution</c> and <c>cancelExecutions</c> make: a Pending or InProgress run
/// is flagged, and the page reports what the service answered rather than a fixed message.
/// Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md states the
/// principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class RunCancelTests
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
    public async Task A_pending_run_offers_cancel_on_its_page_and_ends_flagged()
    {
        var runId = await SeedRunAsync("Acme.IWaiting", TrainState.Pending);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        var cancel = page.WaitForElement("button:contains('Cancel')", WaitTimeout);
        await cancel.ClickAsync(new());

        (await IsFlaggedAsync(runId))
            .Should()
            .BeTrue("a Pending run is cancellable through the API, so it is from its page");
    }

    [Test]
    public async Task Cancelling_a_run_that_finished_since_the_page_loaded_reports_the_failure()
    {
        var runId = await SeedRunAsync("Acme.IRacing", TrainState.InProgress);
        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        var cancel = page.WaitForElement("button:contains('Cancel')", WaitTimeout);

        await SetStateAsync(runId, TrainState.Completed);
        await cancel.ClickAsync(new());

        page.WaitForAssertion(
            () =>
                page
                    .Markup.Should()
                    .Contain(
                        $"Execution {runId} is not cancellable (missing or already terminal).",
                        "the API's cancelExecution fails for a finished run, and so does the page"
                    ),
            WaitTimeout
        );
        Messages()
            .Should()
            .NotContain(m => m.Contains("Cancel"), "nothing was cancelled, so nothing says it was");
        (await IsFlaggedAsync(runId)).Should().BeFalse();
    }

    [Test]
    public async Task The_runs_list_offers_cancel_for_a_pending_run()
    {
        var runId = await SeedRunAsync("Acme.IQueued", TrainState.Pending);

        var page = _ctx.RenderComponent<MetadataPage>();
        WaitForRow(page, "IQueued");
        var cancel = Row(page, "IQueued")
            .QuerySelectorAll("button")
            .Should()
            .HaveCount(2, "the row offers view and cancel")
            .And.Subject.Last();
        await cancel.ClickAsync(new());

        (await IsFlaggedAsync(runId)).Should().BeTrue();
        Messages().Should().Contain("Cancellation requested for IQueued.");
    }

    [Test]
    public async Task Cancel_selected_flags_pending_and_in_progress_runs_and_reports_the_count()
    {
        var pending = await SeedRunAsync("Acme.IPending", TrainState.Pending);
        var running = await SeedRunAsync("Acme.IRunning", TrainState.InProgress);
        var done = await SeedRunAsync("Acme.IDone", TrainState.Completed);

        var page = _ctx.RenderComponent<MetadataPage>();
        foreach (var name in new[] { "IPending", "IRunning", "IDone" })
        {
            WaitForRow(page, name);
            await ToggleRow(page, name);
        }

        await ClickButton(page, "Cancel Selected");

        page.WaitForAssertion(
            () => Messages().Should().Contain("Cancellation requested for 2 of 3 execution(s)."),
            WaitTimeout
        );
        (await IsFlaggedAsync(pending)).Should().BeTrue("a Pending run is cancellable");
        (await IsFlaggedAsync(running)).Should().BeTrue();
        (await IsFlaggedAsync(done)).Should().BeFalse("a finished run is not");
    }

    [Test]
    public async Task Cancelling_a_run_a_users_draft_started_is_refused_on_its_page()
    {
        var runId = await SeedRunAsync("Acme.IWizardStep", TrainState.InProgress, userOwned: true);

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));
        var cancel = page.WaitForElement("button:contains('Cancel')", WaitTimeout);
        await cancel.ClickAsync(new());

        page.WaitForAssertion(
            () =>
                page
                    .Markup.Should()
                    .Contain(
                        OperationsService.UserOwnedRunCancelRefusal,
                        "the API's cancelExecution refuses it with the same reason"
                    ),
            WaitTimeout
        );
        (await IsFlaggedAsync(runId)).Should().BeFalse("a user's draft is read-only to operators");
    }

    [Test]
    public async Task Cancel_selected_skips_the_runs_users_drafts_started_and_says_so()
    {
        var users = await SeedRunAsync("Acme.IWizardStep", TrainState.Pending, userOwned: true);
        var plain = await SeedRunAsync("Acme.IPlain", TrainState.Pending);

        var page = _ctx.RenderComponent<MetadataPage>();
        foreach (var name in new[] { "IWizardStep", "IPlain" })
        {
            WaitForRow(page, name);
            await ToggleRow(page, name);
        }

        await ClickButton(page, "Cancel Selected");

        page.WaitForAssertion(
            () =>
                Messages()
                    .Should()
                    .Contain(
                        "Cancellation requested for 1 of 2 execution(s). 1 skipped: "
                            + OperationsService.UserOwnedRunCancelRefusal
                    ),
            WaitTimeout
        );
        (await IsFlaggedAsync(users)).Should().BeFalse();
        (await IsFlaggedAsync(plain)).Should().BeTrue();
    }

    private IEnumerable<string> Messages() =>
        _ctx.Services.GetRequiredService<NotificationService>().Messages.Select(m => m.Detail);

    private async Task<long> SeedRunAsync(string name, TrainState state, bool userOwned = false)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                InvokedBy = userOwned
                    ? new InvokedBy("wizard", Guid.NewGuid(), SnapshotOwnerKind.User)
                    : null,
            }
        );
        run.TrainState = state;
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private async Task SetStateAsync(long id, TrainState state)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = await db.Metadatas.SingleAsync(m => m.Id == id);
        run.TrainState = state;
        await db.SaveChanges(default);
    }

    private async Task<bool> IsFlaggedAsync(long id)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        return (
            await db.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id)
        ).CancellationRequested;
    }
}
