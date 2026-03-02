using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Trains.MetadataCleanup;

/// <summary>
/// Train interface for cleaning up expired metadata entries.
/// </summary>
public interface IMetadataCleanupTrain : IServiceTrain<MetadataCleanupRequest, Unit>;
