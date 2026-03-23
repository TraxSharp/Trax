using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession.Junctions;

namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;

/// <summary>
/// Processes completed EV charging sessions (wired and wireless) at the service plaza.
/// Scheduled per zone via ScheduleMany every 2 minutes.
/// Collects session data, calculates costs at $0.14/kWh, and updates billing.
/// </summary>
[TraxMutation(Description = "Processes a completed EV charging session")]
[TraxBroadcast]
public class ProcessChargingSessionTrain
    : ServiceTrain<ProcessChargingSessionInput, ProcessChargingSessionOutput>,
        IProcessChargingSessionTrain
{
    protected override ProcessChargingSessionOutput Junctions() =>
        Chain<CollectSessionDataJunction>().Chain<CalculateBillingJunction>();
}
