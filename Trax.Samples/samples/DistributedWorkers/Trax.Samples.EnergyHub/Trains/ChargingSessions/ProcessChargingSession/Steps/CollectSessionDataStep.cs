using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession.Steps;

public class CollectSessionDataStep(ILogger<CollectSessionDataStep> logger)
    : Step<ProcessChargingSessionInput, ProcessChargingSessionInput>
{
    public override async Task<ProcessChargingSessionInput> Run(ProcessChargingSessionInput input)
    {
        logger.LogInformation(
            "[{StationId}] Collecting {SessionType} charging session data",
            input.StationId,
            input.SessionType
        );

        await Task.Delay(200);

        logger.LogInformation(
            "[{StationId}] Found 12 completed sessions since last collection",
            input.StationId
        );

        return input;
    }
}
