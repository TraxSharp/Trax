using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The work queue page marks an entry "Staged" only while a two-phase enqueue has written it
/// and not yet confirmed it: queued with no confirmation time. A cancelled entry also has no
/// confirmation time, because it was cancelled before it was confirmed, but it is not waiting
/// for anything, and a confirmed entry is simply queued.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
public class WorkQueueStagedBadgeTests
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
        _ctx.Services.AddSingleton<IDataContextProviderFactory>(_data);
        _ctx.Services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        _ctx.Services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Only_a_queued_unconfirmed_entry_is_marked_Staged()
    {
        await SeedAsync("subject-staged", WorkQueueStatus.Queued, confirmed: false);
        await SeedAsync("subject-cancelled", WorkQueueStatus.Cancelled, confirmed: false);
        await SeedAsync("subject-confirmed", WorkQueueStatus.Queued, confirmed: true);

        var page = _ctx.RenderComponent<WorkQueuePage>();

        page.WaitForAssertion(
            () => page.FindAll("tr").Count(r => r.TextContent.Contains("subject-")).Should().Be(3),
            TimeSpan.FromSeconds(10)
        );

        IsMarkedStaged(page, "subject-staged")
            .Should()
            .BeTrue("it is queued and its OnQueue hook has not confirmed it yet");
        IsMarkedStaged(page, "subject-cancelled")
            .Should()
            .BeFalse("it was cancelled before it was confirmed, so it waits for nothing");
        IsMarkedStaged(page, "subject-confirmed").Should().BeFalse("it has been confirmed");
    }

    private static bool IsMarkedStaged(IRenderedComponent<WorkQueuePage> page, string subject) =>
        page.FindAll("tr")
            .Single(r => r.TextContent.Contains(subject))
            .QuerySelectorAll(".rz-badge")
            .Any(b => b.TextContent.Trim() == "Staged");

    private async Task SeedAsync(string subject, WorkQueueStatus status, bool confirmed)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Trax.Dashboard.Tests.IStagedTrain",
                DeferPromotion = !confirmed,
                SubjectKey = subject,
            }
        );
        entry.Status = status;

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(entry);
        await db.SaveChanges(default);
    }
}
