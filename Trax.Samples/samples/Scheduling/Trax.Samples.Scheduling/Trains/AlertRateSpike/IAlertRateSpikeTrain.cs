using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduling.Trains.AlertRateSpike;

public interface IAlertRateSpikeTrain : IServiceTrain<AlertRateSpikeInput, Unit>;
