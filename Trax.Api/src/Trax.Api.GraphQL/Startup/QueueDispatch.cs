using HotChocolate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Whether anything can dispatch a run this host queues. A store that is not relational is the EF
/// Core InMemory provider, this process's memory: no job dispatcher runs there (it needs a
/// database) and no other process can reach the store, so a queued entry would never run. A
/// relational store still queues, since a scheduler elsewhere can dispatch it
/// (docs/adr/0038-a-queued-mutation-is-refused-where-nothing-dispatches-it.md). A host with no data
/// provider is not judged here; the enqueue fails in the mediator.
/// </summary>
internal static class QueueDispatch
{
    /// <summary>The error code a refused queue mutation carries.</summary>
    public const string UnavailableCode = "TRAX_QUEUE_UNAVAILABLE";

    /// <summary>What a refused <c>mode: QUEUE</c> answers.</summary>
    public const string UnavailableMessage =
        "Nothing on this server can dispatch a queued run: its store is in memory, so no job "
        + "dispatcher runs. Nothing was queued. Use mode: RUN to run the train now.";

    /// <summary>True when the host's store is in memory, so nothing dispatches its work queue.</summary>
    public static async Task<bool> NothingDispatchesAsync(
        IServiceProvider services,
        CancellationToken ct
    )
    {
        // A database provider registers a SQL dialect; the in-memory one does not.
        if (services.GetService<ISqlDialect>() is not null)
            return false;
        if (services.GetService<IDataContextProviderFactory>() is not { } factory)
            return false;
        using var db = await factory.CreateDbContextAsync(ct);
        return db is DbContext context && !context.Database.IsRelational();
    }

    /// <summary>The refusal a queue mutation throws on such a host.</summary>
    public static GraphQLException Unavailable() =>
        new(ErrorBuilder.New().SetMessage(UnavailableMessage).SetCode(UnavailableCode).Build());
}

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

        await using var scope = services.CreateAsyncScope();
        if (await QueueDispatch.NothingDispatchesAsync(scope.ServiceProvider, cancellationToken))
            logger.LogWarning(
                "This host's store is in memory, so nothing dispatches its work queue, and "
                    + "mode: QUEUE on these train mutations is refused: {Trains}. Expose them as "
                    + "[TraxMutation(GraphQLOperation.Run)] to drop the mode argument, or use a "
                    + "database provider such as UsePostgres().",
                string.Join(", ", queueCapableMutations)
            );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
