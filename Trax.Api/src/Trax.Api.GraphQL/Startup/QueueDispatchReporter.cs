using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Warns once at startup, naming them, when train mutations that can queue are exposed on a host
/// where nothing dispatches the queue. Each such request is refused
/// (<see cref="QueueDispatch.Unavailable"/>); the host still starts, because a bare
/// <c>[TraxMutation]</c> exposes Queue by default and most hosts like this only ever run.
/// </summary>
internal sealed class QueueDispatchReporter(
    IReadOnlyList<string> queueCapableMutations,
    IServiceProvider services,
    ILogger<QueueDispatchReporter> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (queueCapableMutations.Count == 0)
            return;

        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if (
                await QueueDispatch
                    .NothingDispatchesAsync(scope.ServiceProvider, cancellationToken)
                    .ConfigureAwait(false)
            )
                logger.LogWarning(
                    "This host's store is in memory, so nothing dispatches its work queue, and "
                        + "mode: QUEUE on these train mutations is refused: {Trains}. Expose them as "
                        + "[TraxMutation(GraphQLOperation.Run)] to drop the mode argument, or use a "
                        + "database provider such as UsePostgres().",
                    string.Join(", ", queueCapableMutations)
                );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
