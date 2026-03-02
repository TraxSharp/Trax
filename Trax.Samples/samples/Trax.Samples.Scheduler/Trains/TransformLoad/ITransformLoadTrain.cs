using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.TransformLoad;

/// <summary>
/// Interface for the TransformLoad train.
/// Used by the TrainBus for train resolution.
/// </summary>
public interface ITransformLoadTrain : IServiceTrain<TransformLoadInput, Unit>;
