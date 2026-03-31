using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests ScheduleOnce manifests — they fire once when ScheduledAt passes,
/// then auto-disable (IsEnabled = false) after successful execution.
/// </summary>
[TestFixture]
public class ScheduleOnceTests : SchedulerTestFixture
{
    [Test]
    public async Task WelcomeBonus_FiresOnce_ThenAutoDisables()
    {
        // Get a tracked manifest so we can update ScheduledAt to trigger it now.
        var manifest = await DataContext.Manifests.FirstAsync(m =>
            m.ExternalId == ManifestNames.WelcomeBonus
        );

        // Reset to a pristine "ready to fire" state:
        // - ScheduledAt in the past so ManifestManager will enqueue it
        // - IsEnabled = true so it's eligible for scheduling
        // - LastSuccessfulRun = null so the Once logic considers it unfired
        manifest.ScheduledAt = DateTime.UtcNow.AddSeconds(-5);
        manifest.IsEnabled = true;
        manifest.LastSuccessfulRun = null;

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Enable ManifestManager so it detects the overdue Once manifest and enqueues it.
        EnableManifestManager();

        try
        {
            // Wait for the train to complete via scheduler dispatch.
            await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                manifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            // After successful execution, the manifest should be auto-disabled.
            // Poll for IsEnabled=false because the manifest update may commit
            // slightly after the metadata reaches Completed state.
            var manifestDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < manifestDeadline)
            {
                DataContext.Reset();
                var updatedManifest = await DataContext
                    .Manifests.AsNoTracking()
                    .FirstAsync(m => m.ExternalId == ManifestNames.WelcomeBonus);

                if (!updatedManifest.IsEnabled)
                    return; // Test passes — manifest was auto-disabled.

                await Task.Delay(250);
            }

            Assert.Fail("Once manifest was not auto-disabled within 10 seconds after firing");
        }
        finally
        {
            DisableManifestManager();
        }
    }
}
