using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Scheduler.Trains.AlwaysFails;

/// <summary>
/// Interface for the AlwaysFails train.
/// This train always throws an exception, which causes it to dead-letter
/// after exhausting its retry budget—useful for testing dead letter resolution.
/// </summary>
public interface IAlwaysFailsTrain : IServiceTrain<AlwaysFailsInput, Unit>;
