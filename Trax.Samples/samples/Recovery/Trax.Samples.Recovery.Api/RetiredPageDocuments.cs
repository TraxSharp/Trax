using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Samples.Recovery.Api;

/// <summary>
/// Deactivates the documents an earlier version of this host registered for the page by hand, under ids starting with
/// <c>recovery_</c>. The page now runs on the client <c>trax client</c> generates, whose documents the host generates
/// and registers itself, so a client still sending an old id is refused rather than served a document nobody keeps.
/// </summary>
public sealed class RetiredPageDocuments(
    IPersistedOperationStore store,
    ILogger<RetiredPageDocuments> logger
) : IHostedService
{
    /// <summary>The prefix the retired page documents were registered under.</summary>
    public const string Prefix = "recovery_";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var stored in await store.ListAsync(tenantKey: null, cancellationToken))
        {
            if (!stored.IsActive || !stored.Id.StartsWith(Prefix, StringComparison.Ordinal))
                continue;
            await store.DeactivateAsync(
                stored.Id,
                tenantKey: null,
                "The Recovery page runs on its generated client; this hand-written document is retired.",
                cancellationToken
            );
            logger.LogInformation("Deactivated the retired page document {Id}.", stored.Id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
