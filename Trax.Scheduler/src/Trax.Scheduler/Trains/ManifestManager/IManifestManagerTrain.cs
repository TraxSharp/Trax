using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// Interface for the ManifestManagerTrain which orchestrates the manifest-based job scheduling system.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-scheduler#infrastructure-types">SDK reference</seealso>
public interface IManifestManagerTrain : IServiceTrain<Unit, Unit>;
