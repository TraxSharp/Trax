using HotChocolate.Execution;
using HotChocolate.Execution.Caching;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Invalidates HotChocolate's request-pipeline caches when a persisted
/// operation document changes. Without this, HC's <see cref="IDocumentCache"/>
/// (parsed <c>DocumentNode</c> keyed by persisted-op id) and
/// <see cref="IPreparedOperationCache"/> (compiled operation keyed by
/// <c>{schema}-{executorVersion}-{documentId}+{operationName}</c>) will keep
/// serving the previously cached version even after
/// <c>IOperationDocumentStorage.TryReadAsync</c> returns the new text.
/// </summary>
/// <remarks>
/// Neither cache exposes per-id removal in HC 15.x, so this clears each
/// cache in its entirety. Persisted-operation upserts are operator-driven
/// and rare, so the cache-warm cost on the next handful of requests is
/// acceptable.
/// </remarks>
internal sealed class HotChocolateOperationCacheInvalidator
{
    private readonly IServiceProvider _rootServices;
    private readonly ILogger<HotChocolateOperationCacheInvalidator> _logger;
    private volatile string? _schemaName;

    public HotChocolateOperationCacheInvalidator(
        IServiceProvider rootServices,
        ILogger<HotChocolateOperationCacheInvalidator> logger
    )
    {
        ArgumentNullException.ThrowIfNull(rootServices);
        ArgumentNullException.ThrowIfNull(logger);
        _rootServices = rootServices;
        _logger = logger;
    }

    /// <summary>
    /// Schema name captured at <c>ConfigureSchema</c> time so we know which
    /// executor to ask for. <c>null</c> resolves to HC's default schema.
    /// </summary>
    public void SetSchemaName(string? schemaName) => _schemaName = schemaName;

    /// <summary>
    /// Clears both the parsed-document cache and the prepared-operation
    /// cache. Safe to call from any thread. Never throws; logs and returns.
    /// </summary>
    public async Task InvalidateAsync(CancellationToken ct)
    {
        try
        {
            var documentCache = _rootServices.GetService<IDocumentCache>();
            documentCache?.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clear HotChocolate IDocumentCache during persisted-operation invalidation."
            );
        }

        try
        {
            var resolver = _rootServices.GetService<IRequestExecutorResolver>();
            if (resolver is null)
                return;

            var executor = await resolver
                .GetRequestExecutorAsync(_schemaName, ct)
                .ConfigureAwait(false);
            var prepared = executor.Schema.Services.GetService<IPreparedOperationCache>();
            prepared?.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clear HotChocolate IPreparedOperationCache during persisted-operation invalidation."
            );
        }
    }
}
