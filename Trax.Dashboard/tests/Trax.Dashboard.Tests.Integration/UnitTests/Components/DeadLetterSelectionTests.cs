using Bunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The dead letter page's batch re-queue and acknowledge act on the dead letters the operator has
/// ticked, followed by id across the server-paged grid's page changes, and emptying the selection
/// resumes the polling a selection pauses.
/// </summary>
[TestFixture]
public class DeadLetterSelectionTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private RecordingScheduler _scheduler = null!;

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
            method == nameof(ITraxScheduler.RequeueDeadLettersAsync)
                ? Task.FromResult(new BatchDeadLetterResult(((long[])args[0]!).Length, "re-queued"))
                : throw new InvalidOperationException($"{method} was not expected.");
        _scheduler = recorder;
        _ctx.Services.AddSingleton(scheduler);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Requeue_selected_sends_only_the_dead_letters_ticked_now()
    {
        var ids = await SeedDeadLettersAsync(25);

        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "reason-25");

        // Tick reason-25, page away and back, untick it, and tick reason-24 instead.
        await ToggleRow(page, "reason-25");
        await NextPage(page, "reason-05");
        await PreviousPage(page, "reason-25");
        if (!IsTicked(page, "reason-25"))
            await ToggleRow(page, "reason-25");
        await ToggleRow(page, "reason-25");
        await ToggleRow(page, "reason-24");

        await ClickButton(page, "Requeue Selected");

        _scheduler
            .CallsTo(nameof(ITraxScheduler.RequeueDeadLettersAsync))
            .Single()[0]
            .Should()
            .BeEquivalentTo(
                new[] { ids[24 - 1] },
                "only reason-24 is ticked; reason-25 was unticked before the batch was sent"
            );
    }

    [Test]
    public async Task Clearing_the_selection_from_the_header_resumes_polling()
    {
        await _ctx
            .Services.GetRequiredService<IDashboardSettingsService>()
            .SetPollingIntervalAsync(1);
        await SeedDeadLettersAsync(1);

        var page = _ctx.RenderComponent<DeadLettersPage>();
        WaitForRow(page, "reason-01");

        await ToggleRow(page, "reason-01");
        await page.Find("thead .rz-chkbox-box").ClickAsync(new());
        ButtonText(page, "Requeue Selected").Should().BeEmpty("the premise is a cleared selection");

        // A dead letter that arrives after the selection is cleared shows on a later poll.
        await SeedDeadLetterAsync("reason-arrived");
        WaitForRow(page, "reason-arrived");
    }

    private async Task<long[]> SeedDeadLettersAsync(int count)
    {
        var ids = new long[count];
        for (var i = 1; i <= count; i++)
            ids[i - 1] = await SeedDeadLetterAsync($"reason-{i:00}");
        return ids;
    }

    private async Task<long> SeedDeadLetterAsync(string reason)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var manifest = await ((DbContext)db).Set<Manifest>().FirstOrDefaultAsync();
        if (manifest is null)
        {
            var group = new ManifestGroup { Name = "dead" };
            await db.Track(group);
            await db.SaveChanges(default);
            manifest = Manifest.Create(new CreateManifest { Name = typeof(IDeadTrain) });
            manifest.ManifestGroupId = group.Id;
            await db.Track(manifest);
            await db.SaveChanges(default);
        }

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = reason,
                RetryCount = 3,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    public interface IDeadTrain { }
}
