using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Samples.EnergyHub.E2E.Fixtures;
using Trax.Samples.EnergyHub.E2E.Utilities;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class DependencyChainTests : HubTestFixture
{
    [Test]
    public async Task SolarProduction_TriggersBatteryManagement()
    {
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        EnableManifestManager();

        try
        {
            var solarManifest = await EnqueueSolarProductionAsync();

            // Wait for solar production to complete.
            await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                solarManifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            // The ManifestManager should detect solar completion and enqueue the
            // dependent ManageBatteryStorage train.
            var batteryManifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.ManageBatteryStorage);

            var metadata = await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                batteryManifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            metadata.Should().NotBeNull();
            metadata.Name.Should().Contain("ManageBatteryStorage");
        }
        finally
        {
            DisableManifestManager();
        }
    }

    private async Task<Trax.Effect.Models.Manifest.Manifest> EnqueueSolarProductionAsync()
    {
        // Find the solar manifest and enqueue work for it.
        var solarManifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.MonitorSolarProduction)
            .ConfigureAwait(false);

        var input = new MonitorSolarProductionInput { ArrayId = "SPA-001", Region = "somerset" };

        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = solarManifest.Name,
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(MonitorSolarProductionInput).FullName,
                ManifestId = solarManifest.Id,
                Priority = 20,
            },
            TimeProvider.System
        );

        await DataContext.Track(entry).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();

        return solarManifest;
    }
}
