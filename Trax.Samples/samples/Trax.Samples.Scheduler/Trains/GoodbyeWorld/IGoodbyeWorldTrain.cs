using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.GoodbyeWorld;

/// <summary>
/// Interface for the GoodbyeWorld train.
/// Used by the TrainBus for train resolution.
/// </summary>
public interface IGoodbyeWorldTrain : IServiceTrain<GoodbyeWorldInput, Unit>;
