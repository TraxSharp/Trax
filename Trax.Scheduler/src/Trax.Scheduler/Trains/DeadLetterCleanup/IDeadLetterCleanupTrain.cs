using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.DeadLetterCleanup;

/// <summary>
/// Service train interface for the dead letter cleanup train.
/// </summary>
public interface IDeadLetterCleanupTrain : IServiceTrain<DeadLetterCleanupRequest, Unit>;
