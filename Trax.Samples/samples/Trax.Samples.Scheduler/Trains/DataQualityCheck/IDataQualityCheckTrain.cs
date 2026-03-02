using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.DataQualityCheck;

/// <summary>
/// Interface for the DataQualityCheck train.
/// Used by the TrainBus for train resolution.
/// </summary>
public interface IDataQualityCheckTrain : IServiceTrain<DataQualityCheckInput, Unit>;
