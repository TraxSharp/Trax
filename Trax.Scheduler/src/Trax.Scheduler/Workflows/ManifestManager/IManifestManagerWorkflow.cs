using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Scheduler.Workflows.ManifestManager;

/// <summary>
/// Interface for the ManifestManagerWorkflow which orchestrates the manifest-based job scheduling system.
/// </summary>
public interface IManifestManagerWorkflow : IServiceTrain<Unit, Unit>;
