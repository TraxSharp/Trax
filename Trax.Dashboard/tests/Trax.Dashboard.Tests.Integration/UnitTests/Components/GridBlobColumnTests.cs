using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.TraxScheduler;
using static Trax.Dashboard.Tests.Integration.Utils.GridInteraction;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The grids read rows without the large text columns they never show (a run's input, output
/// and stack trace, a work queue entry's input, a manifest's properties), because they re-read
/// their page on every poll tick. Each grid reads a projected row type, so EF Core builds no
/// entity for it: a page that read whole entities shows up here as materialized entities. The
/// SQL itself is checked against Postgres in the stress suite.
/// </summary>
[TestFixture]
public class GridBlobColumnTests
{
    private const int InputBytes = 64 * 1024;

    private Bunit.TestContext _ctx = null!;
    private MaterializationCounter _reads = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _reads = new MaterializationCounter();
        _data = new InMemoryDataContextFactory(_reads);
        _ctx.Services.AddDashboardPageServices(_data);
        _ctx.Services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_runs_list_reads_no_run_entities()
    {
        await SeedRunsAsync(manifestId: null, TrainState.Completed, 25);
        _reads.Reset();

        var page = _ctx.RenderComponent<MetadataPage>();

        WaitForRows(page, 20);
        _reads.CountOf<Metadata>().Should().Be(0, "the list reads rows without the input");
    }

    [Test]
    public async Task The_work_queue_list_reads_no_entries_whole()
    {
        await using (var db = await _data.CreateDbContextAsync(default))
        {
            for (var i = 0; i < 25; i++)
                db.WorkQueues.Add(
                    WorkQueue.Create(
                        new CreateWorkQueue
                        {
                            TrainName = "T",
                            Input = BigJson(),
                            SubjectKey = $"s{i}",
                        }
                    )
                );
            await db.SaveChanges(default);
        }
        _reads.Reset();

        var page = _ctx.RenderComponent<WorkQueuePage>();

        WaitForRows(page, 20);
        _reads.CountOf<WorkQueue>().Should().Be(0, "the list reads rows without the input");
    }

    [Test]
    public async Task The_manifests_list_reads_no_manifests_whole_and_shows_the_group()
    {
        await SeedManifestAsync("nightly");
        _reads.Reset();

        var page = _ctx.RenderComponent<ManifestsPage>();

        WaitForRow(page, "nightly");
        _reads.CountOf<Manifest>().Should().Be(0, "the list reads rows without the properties");
    }

    [Test]
    public async Task The_manifest_runs_grid_reads_no_run_entities()
    {
        var manifestId = await SeedManifestAsync("runs");
        await SeedRunsAsync(manifestId, TrainState.Completed, 25);
        _reads.Reset();

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );

        WaitForRows(page, 20);
        _reads.CountOf<Metadata>().Should().Be(0);
    }

    [Test]
    public async Task The_group_runs_grid_reads_no_run_entities()
    {
        var manifestId = await SeedManifestAsync("group-runs");
        await SeedRunsAsync(manifestId, TrainState.Completed, 25);
        long groupId;
        await using (var db = await _data.CreateDbContextAsync(default))
            groupId = (await db.Manifests.SingleAsync(m => m.Id == manifestId)).ManifestGroupId;
        _reads.Reset();

        var page = _ctx.RenderComponent<ManifestGroupDetailPage>(p =>
            p.Add(x => x.ManifestGroupId, groupId)
        );

        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Count.Should().BeGreaterThanOrEqualTo(21),
            WaitTimeout
        );
        _reads.CountOf<Metadata>().Should().Be(0);
    }

    [Test]
    public async Task The_dead_letter_page_reads_its_latest_failed_run_once()
    {
        await _ctx
            .Services.GetRequiredService<IDashboardSettingsService>()
            .SetPollingIntervalAsync(1);
        var manifestId = await SeedManifestAsync("dead");
        await SeedRunsAsync(manifestId, TrainState.Failed, 25);
        var deadLetterId = await SeedDeadLetterAsync(manifestId);
        _reads.Reset();

        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p =>
            p.Add(x => x.DeadLetterId, deadLetterId)
        );
        WaitForRows(page, 20);
        _reads
            .CountOf<Metadata>()
            .Should()
            .Be(1, "the latest failed run is shown whole; the grid reads rows without the input");

        // Two poll ticks later the latest failed run is the same one, so it is not read again.
        var settings = _ctx.Services.GetRequiredService<IDashboardSettingsService>();
        var loaded = settings.LastPollTime;
        page.WaitForAssertion(() => settings.LastPollTime.Should().BeAfter(loaded), WaitTimeout);
        var ticked = settings.LastPollTime;
        page.WaitForAssertion(() => settings.LastPollTime.Should().BeAfter(ticked), WaitTimeout);
        _reads.CountOf<Metadata>().Should().Be(1, "an unchanged failed run is not read again");
    }

    private static void WaitForRows(IRenderedFragment page, int count) =>
        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Count.Should().Be(count),
            WaitTimeout
        );

    private static string BigJson() => $"{{\"blob\":\"{new string('x', InputBytes)}\"}}";

    private async Task<long> SeedManifestAsync(string groupName)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = groupName };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IBlobTrain) });
        manifest.ManifestGroupId = group.Id;
        manifest.Properties = BigJson();
        await db.Track(manifest);
        await db.SaveChanges(default);
        return manifest.Id;
    }

    private async Task SeedRunsAsync(long? manifestId, TrainState state, int count)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(IBlobTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                    ManifestId = manifestId,
                }
            );
            run.TrainState = state;
            run.Input = BigJson();
            run.Output = BigJson();
            await db.Track(run);
        }
        await db.SaveChanges(default);
    }

    private async Task<long> SeedDeadLetterAsync(long manifestId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var manifest = await ((DbContext)db).Set<Manifest>().FindAsync(manifestId);
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest!,
                Reason = "retries exhausted",
                RetryCount = 3,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    public interface IBlobTrain { }
}
