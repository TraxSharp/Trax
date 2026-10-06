using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The work queue entry page reads the entry through
/// <c>IOperationsService.GetWorkQueueEntryDetailAsync</c>, the call behind the API's
/// <c>workQueue.detail</c>, and shows what a queued entry with a subject is waiting on: the entry
/// whose run holds the subject, or the sibling dispatch would offer first. Enforces
/// <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class WorkQueueDetailPageTests
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
    public async Task A_queued_entry_behind_an_older_sibling_names_it()
    {
        var older = await AddEntryAsync("customer-1", createdAt: DateTime.UtcNow.AddMinutes(-5));
        var waiting = await AddEntryAsync("customer-1");

        var page = Render(waiting);

        page.WaitForAssertion(
            () =>
                page
                    .Markup.Should()
                    .Contain($"Entry {older}, which is ahead of it for the same subject"),
            TimeSpan.FromSeconds(10)
        );
        page.Markup.Should().Contain("customer-1");
    }

    [Test]
    public async Task A_queued_entry_whose_subject_has_a_run_in_flight_names_the_entry_holding_it()
    {
        var run = await AddRunAsync(TrainState.InProgress);
        var holder = await AddEntryAsync("customer-2", WorkQueueStatus.Dispatched, metadataId: run);
        await AddEntryAsync("customer-2", createdAt: DateTime.UtcNow.AddMinutes(-5));
        var waiting = await AddEntryAsync("customer-2");

        var page = Render(waiting);

        page.WaitForAssertion(
            () =>
                page
                    .Markup.Should()
                    .Contain($"Entry {holder}, which is running for the same subject")
                    .And.NotContain("which is ahead of it"),
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task An_entry_with_nothing_ahead_names_nothing()
    {
        var alone = await AddEntryAsync("customer-3");

        var page = Render(alone);

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("customer-3"),
            TimeSpan.FromSeconds(10)
        );
        page.Markup.Should().NotContain("Waiting On");
    }

    private IRenderedComponent<WorkQueueDetailPage> Render(long id) =>
        _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, id));

    private async Task<long> AddEntryAsync(
        string subject,
        WorkQueueStatus status = WorkQueueStatus.Queued,
        long? metadataId = null,
        DateTime? createdAt = null
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Acme.ISubjectTrain",
                InputTypeName = "Acme.SubjectInput",
                SubjectKey = subject,
            }
        );
        entry.Status = status;
        entry.MetadataId = metadataId;
        if (createdAt is { } at)
            entry.CreatedAt = at;

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(entry);
        await db.SaveChanges(default);
        return entry.Id;
    }

    private async Task<long> AddRunAsync(TrainState state)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = "Acme.ISubjectTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = state;

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }
}
