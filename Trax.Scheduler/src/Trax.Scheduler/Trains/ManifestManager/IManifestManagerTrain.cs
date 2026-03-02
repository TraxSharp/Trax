using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.ManifestManager;

/// <summary>
/// Interface for the ManifestManagerTrain which orchestrates the manifest-based job scheduling system.
/// </summary>
public interface IManifestManagerTrain : IServiceTrain<Unit, Unit>;
