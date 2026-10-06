using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession.Junctions;

public class CalculateBillingJunction(ILogger<CalculateBillingJunction> logger)
    : Junction<ProcessChargingSessionInput, ProcessChargingSessionOutput>
{
    public override async Task<ProcessChargingSessionOutput> Run(ProcessChargingSessionInput input)
    {
        logger.LogInformation(
            "[{StationId}] Calculating billing at $0.14/kWh for {SessionType} sessions",
            input.StationId,
            input.SessionType
        );

        await Task.Delay(150);

        var sessions = 12;
        var revenue = 23.52m;

        logger.LogInformation(
            "[{StationId}] Billing complete — {Sessions} sessions, ${Revenue:F2} revenue",
            input.StationId,
            sessions,
            revenue
        );

        return new ProcessChargingSessionOutput
        {
            StationId = input.StationId,
            SessionsProcessed = sessions,
            RevenueGenerated = revenue,
        };
    }
}
