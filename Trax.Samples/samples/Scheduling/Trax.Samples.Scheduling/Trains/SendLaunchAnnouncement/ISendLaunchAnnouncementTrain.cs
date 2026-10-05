using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement;

public interface ISendLaunchAnnouncementTrain : IServiceTrain<SendLaunchAnnouncementInput, Unit>;
