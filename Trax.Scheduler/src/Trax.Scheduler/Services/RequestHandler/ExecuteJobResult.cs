using System.ComponentModel;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Services.RequestHandler;

/// <summary>
/// Result of a successful job execution via <see cref="ITraxRequestHandler.ExecuteJobAsync"/>.
/// </summary>
/// <param name="MetadataId">The metadata ID of the executed job</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-job-runner#infrastructure-types">SDK reference</seealso>
public record ExecuteJobResult(long MetadataId);
