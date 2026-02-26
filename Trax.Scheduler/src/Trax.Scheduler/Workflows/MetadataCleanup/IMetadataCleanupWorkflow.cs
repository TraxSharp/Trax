using Trax.Effect.Services.ServiceTrain;
using LanguageExt;

namespace Trax.Scheduler.Workflows.MetadataCleanup;

/// <summary>
/// Workflow interface for cleaning up expired metadata entries.
/// </summary>
public interface IMetadataCleanupWorkflow : IServiceTrain<MetadataCleanupRequest, Unit>;
