using Trax.Effect.Models.Manifest;

namespace Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

public record CorruptedDataRepairInput : IManifestProperties
{
    public required string TableName { get; init; }
}
