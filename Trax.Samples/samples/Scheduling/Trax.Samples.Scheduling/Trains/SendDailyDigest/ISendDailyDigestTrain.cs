using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduling.Trains.SendDailyDigest;

public interface ISendDailyDigestTrain : IServiceTrain<SendDailyDigestInput, Unit>;
