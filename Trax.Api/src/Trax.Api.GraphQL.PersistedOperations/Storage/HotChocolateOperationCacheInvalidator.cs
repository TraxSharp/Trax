using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Caching;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Empties HotChocolate's request-pipeline caches when a persisted operation document
/// changes. Without this, the parsed-document cache (keyed by persisted-op id) and the
/// prepared-operation cache (keyed by
/// <c>{schema}-{executorVersion}-{documentId}+{operationName}</c>) keep serving the
/// previous document even after <c>IOperationDocumentStorage.TryReadAsync</c> returns the
/// new text.
/// </summary>
/// <remarks>
/// Both caches live in the executor's service provider, and HotChocolate 16 exposes no way
/// to drop an entry from its own implementations: <c>Clear()</c> is gone from both, and
/// evicting the executor does not rebuild one whose schema has not changed. The
/// persisted-operations package therefore substitutes
/// <see cref="ClearableDocumentCache"/> and <see cref="ClearablePreparedOperationCache"/>,
/// and this type empties them.
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
    /// Schema name captured at <c>ConfigureSchema</c> time so we know which executor's
    /// caches to reach. <c>null</c> resolves to HotChocolate's default schema.
    /// </summary>
    public void SetSchemaName(string? schemaName) => _schemaName = schemaName;

    /// <summary>
    /// Empties both caches. Safe to call from any thread. Never throws except on
    /// cancellation; otherwise logs and returns.
    /// </summary>
    public async Task InvalidateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var provider = _rootServices.GetService<IRequestExecutorProvider>();
            if (provider is null)
                return;

            var executor = await provider
                .GetExecutorAsync(_schemaName ?? ISchemaDefinition.DefaultName, ct)
                .ConfigureAwait(false);

            var schemaServices = executor.Schema.Services;

            (schemaServices.GetService<IDocumentCache>() as ClearableDocumentCache)?.Clear();
            (
                schemaServices.GetService<IPreparedOperationCache>()
                as ClearablePreparedOperationCache
            )?.Clear();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clear the HotChocolate operation caches during persisted-operation invalidation."
            );
        }
    }
}
