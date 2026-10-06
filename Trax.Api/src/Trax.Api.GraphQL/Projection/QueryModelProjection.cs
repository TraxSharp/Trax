using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;

namespace Trax.Api.GraphQL.Projection;

/// <summary>
/// The projection step for <c>[TraxQueryModel]</c> query fields: narrows the SELECT to
/// the columns the caller's selection set actually names.
/// </summary>
/// <remarks>
/// This replaces HotChocolate's <c>UseProjection</c> middleware with the execution-time
/// projection subsystem (<c>ISelection.AsSelector</c>). The two build equivalent
/// <c>Select</c> expressions, but only the execution-time one honours field requirements,
/// which is what lets a hand-written <c>[ExtendObjectType]</c> resolver read a property of
/// its <c>[Parent]</c> that the caller did not select. See
/// <see cref="QueryModelProjectionRequirementInterceptor"/> for how those requirements are
/// declared.
/// <para>
/// The middleware sits in the same pipeline slot <c>UseProjection</c> occupied — after
/// paging, before filtering and sorting — so the operations still compose in the order
/// filter, sort, project. Applying the <c>Select</c> inside the field resolver instead
/// would run it before the filter and sort middleware and break both: they would be
/// filtering and ordering a projection that no longer carries the columns they reference.
/// </para>
/// </remarks>
internal static class QueryModelProjection
{
    /// <summary>
    /// Builds the projection middleware for one entity type.
    /// </summary>
    public static FieldMiddleware CreateMiddleware<TEntity>()
        where TEntity : class =>
        next =>
            async context =>
            {
                await next(context).ConfigureAwait(false);

                // A selection set that names no field of the entity — `{ totalCount }` on
                // a connection, for example — has nothing to project, and asking for a
                // selector would yield nothing to Select over.
                if (
                    context.Result is IQueryable<TEntity> queryable
                    && context.Selection.AsSelector<TEntity>() is { } selector
                )
                {
                    context.Result = queryable.Select(selector);
                }
            };
}
