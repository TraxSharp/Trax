using GreenDonut;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Batches every manifest a request asks the schedule of into one
/// <see cref="IOperationsService.GetUpcomingRunsAsync"/> call per
/// <see cref="OperationsService.MaxBatchSize"/> ids, each at the most times a field can ask for.
/// </summary>
internal sealed class ManifestUpcomingRunsLoader(
    IOperationsService operations,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options
) : BatchDataLoader<long, ManifestUpcomingRuns>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<long, ManifestUpcomingRuns>> LoadBatchAsync(
        IReadOnlyList<long> keys,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<long, ManifestUpcomingRuns>(keys.Count);
        foreach (var chunk in keys.Chunk(OperationsService.MaxBatchSize))
        {
            var rows = await operations
                .GetUpcomingRunsAsync(chunk, OperationsService.MaxUpcomingRuns, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
                result[row.ManifestId] = row;
        }
        return result;
    }
}
