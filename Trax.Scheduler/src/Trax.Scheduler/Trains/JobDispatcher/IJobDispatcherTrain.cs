using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// Interface for the JobDispatcherTrain which picks queued work queue entries
/// and dispatches them to the background task server.
/// </summary>
public interface IJobDispatcherTrain : IServiceTrain<Unit, Unit>;
