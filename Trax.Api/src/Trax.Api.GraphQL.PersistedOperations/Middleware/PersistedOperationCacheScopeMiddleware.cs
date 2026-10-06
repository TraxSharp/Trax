using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Opens a <see cref="PersistedOperationRequestScope"/> around the rest of the request pipeline,
/// so every cache the request fills knows which generation it started in.
/// </summary>
/// <remarks>
/// Runs before HotChocolate's document cache, which adds to its cache only after the rest of the
/// pipeline has returned, and before the persisted-operation read. See
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// </remarks>
internal static class PersistedOperationCacheScopeMiddleware
{
    public const string Key = "Trax.PersistedOperationCacheScope";

    public static RequestDelegate Create(
        RequestMiddlewareFactoryContext factory,
        RequestDelegate next
    )
    {
        var generation = factory.Services.GetRequiredService<PersistedOperationCacheGeneration>();
        return context => InvokeAsync(context, generation, next);
    }

    // An async method of its own, so the scope set here flows into the rest of the pipeline and
    // is restored for the caller when this returns.
    private static async ValueTask InvokeAsync(
        RequestContext context,
        PersistedOperationCacheGeneration generation,
        RequestDelegate next
    )
    {
        PersistedOperationRequestScope.Begin(generation, context.Request.Document is not null);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            PersistedOperationRequestScope.End();
        }
    }
}
