using GreenDonut;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// <c>nextRuns(count:)</c> and <c>scheduleText</c> on <c>operations.manifests</c> and
/// <c>operations.manifestDetail</c>: when a manifest runs next, and its schedule in plain words.
/// Both read <see cref="IOperationsService.GetUpcomingRunsAsync"/>, once per request for every
/// manifest the request shows, through <see cref="ManifestUpcomingRunsLoader"/>.
/// </summary>
internal static class ManifestScheduleFields
{
    /// <summary>How many upcoming runs <c>nextRuns</c> lists when no count is given.</summary>
    public const int DefaultNextRuns = 5;

    /// <summary>The fields on a manifests page row.</summary>
    public static ObjectTypeExtension<ManifestSummary> Summary() => new(d => Add(d, m => m.Id));

    /// <summary>The fields on a manifest's detail.</summary>
    public static ObjectTypeExtension<ManifestDetail> Detail() => new(d => Add(d, m => m.Id));

    private static void Add<T>(IObjectTypeDescriptor<T> d, Func<T, long> id)
    {
        d.Field("nextRuns")
            .Description(
                "The next times the scheduler will run this manifest, earliest first, in UTC, read "
                    + "from the manifest manager's own due-ness check: exclusion windows and the "
                    + "misfire policy apply, and each run is assumed to succeed when it is due. The "
                    + "first is now when the manifest is due already. Empty for a manual, on-demand, "
                    + "dependent or dormant dependent manifest (scheduleText says what starts it), "
                    + "and for one that is disabled, in a disabled group, or has a dead letter "
                    + "awaiting intervention."
            )
            .Argument(
                "count",
                a =>
                    a.Type<NonNullType<IntType>>()
                        .DefaultValue(DefaultNextRuns)
                        .Description(
                            $"How many times to list, clamped to 0 through {OperationsService.MaxUpcomingRuns}."
                        )
            )
            .Type<NonNullType<ListType<NonNullType<DateTimeType>>>>()
            .Resolve(async ctx =>
            {
                var count = Math.Clamp(
                    ctx.ArgumentValue<int>("count"),
                    0,
                    OperationsService.MaxUpcomingRuns
                );
                var upcoming = await LoadAsync(ctx, id(ctx.Parent<T>()));
                return (upcoming?.NextRuns ?? []).Take(count).ToList();
            });

        d.Field("scheduleText")
            .Description(
                "The schedule in plain words: \"Daily at 02:00 UTC\", \"Every 5 minutes\", "
                    + "\"After nightly-import succeeds\", \"On demand\". A cron expression with no plain "
                    + "reading is shown as itself, in UTC."
            )
            .Type<NonNullType<StringType>>()
            .Resolve(async ctx =>
                (await LoadAsync(ctx, id(ctx.Parent<T>())))?.ScheduleText ?? string.Empty
            );
    }

    private static Task<ManifestUpcomingRuns?> LoadAsync(IResolverContext ctx, long manifestId) =>
        ctx
            .Services.GetRequiredService<ManifestUpcomingRunsLoader>()
            .LoadAsync(manifestId, ctx.RequestAborted);
}

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
            var rows = await operations.GetUpcomingRunsAsync(
                chunk,
                OperationsService.MaxUpcomingRuns,
                cancellationToken
            );
            foreach (var row in rows)
                result[row.ManifestId] = row;
        }
        return result;
    }
}
