using LanguageExt;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Workflows.MetadataCleanup;

/// <summary>
/// Workflow interface for cleaning up expired metadata entries.
/// </summary>
public interface IMetadataCleanupWorkflow : IServiceTrain<MetadataCleanupRequest, Unit>;
