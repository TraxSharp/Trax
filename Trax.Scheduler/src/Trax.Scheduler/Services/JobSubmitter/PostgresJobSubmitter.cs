using System.Text.Json;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// Built-in PostgreSQL implementation of <see cref="IJobSubmitter"/>.
/// </summary>
/// <remarks>
/// Enqueues jobs by inserting into the <c>trax.background_job</c> table.
/// Jobs are picked up by <see cref="LocalWorkerService.LocalWorkerService"/>
/// which polls the table using <c>FOR UPDATE SKIP LOCKED</c> for atomic dequeue.
/// </remarks>
public class PostgresJobSubmitter(IDataContext dataContext) : IJobSubmitter
{
    /// <inheritdoc />
    public Task<string> EnqueueAsync(long metadataId) =>
        EnqueueAsync(metadataId, CancellationToken.None);

    /// <inheritdoc />
    public Task<string> EnqueueAsync(long metadataId, object input) =>
        EnqueueAsync(metadataId, input, CancellationToken.None);

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken)
    {
        var job = BackgroundJob.Create(new CreateBackgroundJob { MetadataId = metadataId });

        await dataContext.Track(job);
        await dataContext.SaveChanges(cancellationToken);

        return job.Id.ToString();
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(
        long metadataId,
        object input,
        CancellationToken cancellationToken
    )
    {
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadataId,
                Input = inputJson,
                InputType = input.GetType().FullName,
            }
        );

        await dataContext.Track(job);
        await dataContext.SaveChanges(cancellationToken);

        return job.Id.ToString();
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(
        long metadataId,
        int priority,
        CancellationToken cancellationToken
    )
    {
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = metadataId, Priority = priority }
        );

        await dataContext.Track(job);
        await dataContext.SaveChanges(cancellationToken);

        return job.Id.ToString();
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(
        long metadataId,
        object input,
        int priority,
        CancellationToken cancellationToken
    )
    {
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = metadataId,
                Input = inputJson,
                InputType = input.GetType().FullName,
                Priority = priority,
            }
        );

        await dataContext.Track(job);
        await dataContext.SaveChanges(cancellationToken);

        return job.Id.ToString();
    }
}
