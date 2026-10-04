using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.SchedulerConfig;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// The in-memory provider keeps the scheduler's columns the way the database providers do, so a
/// scheduler test on it sees the same rows.
/// </summary>
public class InMemorySchedulerColumnTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    private async Task<IDataContext> NewContext() =>
        await Scope
            .ServiceProvider.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None);

    [Test]
    public async Task The_scheduler_columns_are_kept()
    {
        long manifestId,
            entryId;
        using (var context = await NewContext())
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(InMemorySchedulerColumnTests),
                    FailureWindowSeconds = 900,
                    Owner = "orders",
                    ReplayDecisionsOnRetry = false,
                }
            );
            await context.Track(manifest);
            await context.SaveChanges(CancellationToken.None);
            manifestId = manifest.Id;

            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = manifest.Name,
                    ManifestId = manifest.Id,
                    ExplicitTrigger = true,
                }
            );
            await context.Track(entry);

            context.SchedulerConfigs.RemoveRange(await context.SchedulerConfigs.ToListAsync());
            var row = new SchedulerConfig();
            row.SetOverride("MaxActiveJobs", 20);
            context.SchedulerConfigs.Add(row);

            await context.SaveChanges(CancellationToken.None);
            entryId = entry.Id;
        }

        using var readBack = await NewContext();
        var storedManifest = await readBack
            .Manifests.AsNoTracking()
            .SingleAsync(m => m.Id == manifestId);
        var storedEntry = await readBack
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == entryId);
        var storedRow = await readBack.SchedulerConfigs.AsNoTracking().SingleAsync();

        storedManifest.FailureWindowSeconds.Should().Be(900);
        storedManifest.Owner.Should().Be("orders");
        storedManifest.ReplayDecisionsOnRetry.Should().BeFalse();
        storedEntry.IsExplicitTrigger.Should().BeTrue();
        storedRow.OverriddenSettings.Should().Equal("MaxActiveJobs");
    }
}
