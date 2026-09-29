using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// The dashboard's read views report the numbers the API reports for the same data: counts
/// over every run rather than over a capped list, the same states, the same rule for which
/// trains are administrative, and lists read a page at a time rather than whole.
/// Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md states the
/// principle.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
public class ReadViewParityTests
{
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

        var discovery = new TrainDiscoveryService(new ServiceCollection());
        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
        services.AddSingleton(UnusedService<ITraxScheduler>.Create());
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            _data,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            _data,
            new SchedulerConfiguration(),
            sp.GetRequiredService<ITrainExecutionService>()
        ));
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task The_runs_list_hides_admin_trains_by_full_name_only()
    {
        await SeedRunAsync(typeof(IJobRunnerTrain).FullName!, TrainState.Completed);
        await SeedRunAsync("Acme.Billing.IBatchJobRunnerTrain", TrainState.Completed);

        var page = _ctx.RenderComponent<MetadataPage>();

        page.WaitForAssertion(
            () =>
                page.FindAll("tr.rz-data-row")
                    .Select(r => r.TextContent)
                    .Should()
                    .ContainSingle(
                        "the admin train is hidden and a consumer's train whose name merely ends "
                            + "in an admin train's short name is not, as the API's filter does"
                    )
                    .Which.Should()
                    .Contain("IBatchJobRunnerTrain"),
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task Manifest_detail_counts_every_run()
    {
        var manifestId = await SeedManifestAsync("counted");
        await SeedRunsAsync(manifestId, TrainState.Completed, 500);
        await SeedRunsAsync(manifestId, TrainState.Failed, 3);
        await SeedRunsAsync(manifestId, TrainState.InProgress, 2);

        var page = _ctx.RenderComponent<ManifestDetailPage>(p =>
            p.Add(x => x.ManifestId, manifestId)
        );

        page.WaitForAssertion(
            () =>
            {
                CardValue(page, "Total Runs").Should().Be("505");
                CardValue(page, "Completed").Should().Be("500");
                CardValue(page, "Failed").Should().Be("3");
                CardValue(page, "In Progress").Should().Be("2");
            },
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task Manifest_group_list_reports_in_progress_runs()
    {
        var manifestId = await SeedManifestAsync("busy-group");
        await SeedRunsAsync(manifestId, TrainState.InProgress, 2);
        await SeedRunsAsync(manifestId, TrainState.Completed, 1);

        var page = _ctx.RenderComponent<ManifestGroupsPage>();

        page.WaitForAssertion(
            () =>
            {
                var headers = page.FindAll("thead th").Select(h => h.TextContent.Trim()).ToList();
                var column = headers.FindIndex(h => h == "In Progress");
                column.Should().BeGreaterThan(-1, "the API's group stats report in-progress runs");

                var row = page.FindAll("tr.rz-data-row")
                    .Single(r => r.TextContent.Contains("busy-group"));
                row.QuerySelectorAll("td")
                    .Select(c => c.TextContent.Trim())
                    .ElementAt(column)
                    .Should()
                    .Be("2");
            },
            TimeSpan.FromSeconds(10)
        );
    }

    [Test]
    public async Task Run_detail_reads_one_page_of_logs()
    {
        var runId = await SeedRunAsync("Acme.ILoggingTrain", TrainState.Completed);
        await SeedLogsAsync(runId, 100);
        _reads.Reset();

        var page = _ctx.RenderComponent<MetadataDetailPage>(p => p.Add(x => x.MetadataId, runId));

        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Count.Should().Be(20),
            TimeSpan.FromSeconds(10)
        );
        _reads
            .CountOf<Log>()
            .Should()
            .BeLessThanOrEqualTo(
                20,
                "the API pages logs, so the page reads only the page it shows"
            );
    }

    [Test]
    public async Task Dead_letter_detail_reads_one_page_of_failed_runs()
    {
        var manifestId = await SeedManifestAsync("dead");
        await SeedRunsAsync(manifestId, TrainState.Failed, 100);
        var deadLetterId = await SeedDeadLetterAsync(manifestId);
        _reads.Reset();

        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p =>
            p.Add(x => x.DeadLetterId, deadLetterId)
        );

        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Count.Should().Be(20),
            TimeSpan.FromSeconds(10)
        );
        _reads
            .CountOf<Metadata>()
            .Should()
            .BeLessThanOrEqualTo(
                25,
                "one page of failed runs and the most recent one, not every failed run"
            );
    }

    [Test]
    public async Task Manifests_list_reads_one_page_of_manifests()
    {
        for (var i = 0; i < 60; i++)
            await SeedManifestAsync($"group-{i}");
        _reads.Reset();

        var page = _ctx.RenderComponent<ManifestsPage>();

        page.WaitForAssertion(
            () => page.FindAll("tr.rz-data-row").Count.Should().Be(20),
            TimeSpan.FromSeconds(10)
        );
        _reads
            .CountOf<Manifest>()
            .Should()
            .BeLessThanOrEqualTo(
                20,
                "the API pages manifests, so the page reads only the page it shows"
            );
    }

    private static string CardValue<T>(IRenderedComponent<T> page, string caption)
        where T : IComponent =>
        page.FindAll(".rz-card")
            .Where(c =>
                c.QuerySelectorAll(".rz-text-caption").Any(t => t.TextContent.Trim() == caption)
            )
            .Select(c => c.QuerySelector(".rz-text-h3")!.TextContent.Trim())
            .First();

    private async Task<long> SeedManifestAsync(string groupName)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = groupName };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IScheduledTrain) });
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);
        return manifest.Id;
    }

    private async Task<long> SeedRunAsync(string name, TrainState state, long? manifestId = null)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var run = NewRun(name, state, manifestId);
        await db.Track(run);
        await db.SaveChanges(default);
        return run.Id;
    }

    private async Task SeedRunsAsync(long manifestId, TrainState state, int count)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
            await db.Track(NewRun(typeof(IScheduledTrain).FullName!, state, manifestId));
        await db.SaveChanges(default);
    }

    private static Metadata NewRun(string name, TrainState state, long? manifestId)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifestId,
            }
        );
        run.TrainState = state;
        return run;
    }

    private async Task SeedLogsAsync(long runId, int count)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        for (var i = 0; i < count; i++)
        {
            var log = Log.Create(
                new CreateLog
                {
                    Level = LogLevel.Information,
                    Message = $"line {i}",
                    CategoryName = "Acme",
                    EventId = i,
                }
            );
            await db.Track(log);
            ((Microsoft.EntityFrameworkCore.DbContext)db)
                .Entry(log)
                .Property("MetadataId")
                .CurrentValue = runId;
        }
        await db.SaveChanges(default);
    }

    private async Task<long> SeedDeadLetterAsync(long manifestId)
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var manifest = await ((Microsoft.EntityFrameworkCore.DbContext)db)
            .Set<Manifest>()
            .FindAsync(manifestId);
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

    public interface IScheduledTrain { }
}
