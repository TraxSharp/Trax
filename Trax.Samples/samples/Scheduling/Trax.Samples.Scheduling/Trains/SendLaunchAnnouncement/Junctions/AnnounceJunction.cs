using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Core.Junction;

namespace Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement.Junctions;

public class AnnounceJunction(ILogger<AnnounceJunction> logger)
    : Junction<SendLaunchAnnouncementInput, Unit>
{
    public override Task<Unit> Run(SendLaunchAnnouncementInput input)
    {
        logger.LogInformation("Announced: {Message}", input.Message);
        return Task.FromResult(Unit.Default);
    }
}
