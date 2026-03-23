using LanguageExt;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair.Junctions;

namespace Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

/// <summary>
/// A train that always fails — used to demonstrate dead-letter behavior.
/// Scheduled with MaxRetries(1), so it dead-letters after one retry attempt.
/// Check the dashboard dead letter page to see the failure details.
/// </summary>
public class CorruptedDataRepairTrain
    : ServiceTrain<CorruptedDataRepairInput, Unit>,
        ICorruptedDataRepairTrain
{
    protected override Unit Junctions() => Chain<AttemptRepairJunction>();
}
