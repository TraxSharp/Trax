using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>MaxRetries(n)</c> is the number of retries after the first run: a manifest is dead-lettered
/// on its (n + 1)th counted failure, never before.
///
/// <para>Enforces <c>docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
)]
public class MaxRetriesBoundaryTests : TestSetup
{
    [TestCase(0, 0, false)]
    [TestCase(0, 1, true)]
    [TestCase(1, 1, false)]
    [TestCase(1, 2, true)]
    [TestCase(3, 3, false)]
    [TestCase(3, 4, true)]
    public async Task A_manifest_is_dead_lettered_only_once_its_failures_exceed_its_retries(
        int maxRetries,
        int failures,
        bool deadLettered
    )
    {
        var manifest = await CreateManifest(maxRetries);
        for (var i = 0; i < failures; i++)
            await CreateFailedRun(manifest, DateTime.UtcNow.AddMinutes(-10 - i));

        await Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);

        DataContext.Reset();
        var count = await DataContext
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.ManifestId == manifest.Id);
        count
            .Should()
            .Be(
                deadLettered ? 1 : 0,
                $"MaxRetries({maxRetries}) allows {maxRetries + 1} attempts and {failures} failed. See "
                    + "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    private async Task<Manifest> CreateManifest(int maxRetries)
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = maxRetries,
                Properties = new SchedulerTestInput { Value = "boundary" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    private async Task CreateFailedRun(Manifest manifest, DateTime startTime)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "boundary" },
                ManifestId = manifest.Id,
            }
        );
        run.TrainState = TrainState.Failed;
        run.StartTime = startTime;
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }
}
