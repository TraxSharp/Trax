using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.JobDispatcher;

/// <summary>
/// Interface for the JobDispatcherTrain which picks queued work queue entries
/// and dispatches them to the background task server.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#infrastructure-types">SDK reference</seealso>
public interface IJobDispatcherTrain : IServiceTrain<Unit, Unit>;
