using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// Overrides the auto-generated <c>FilterInputType</c> for a specific entity
    /// in the GraphQL schema. Use this when the default filter type exposes
    /// unwanted fields or when property names need remapping.
    /// </summary>
    /// <typeparam name="TEntity">The entity type marked with <c>[TraxQueryModel]</c>.</typeparam>
    /// <typeparam name="TFilter">
    /// A <see cref="FilterInputType{T}"/> subclass that configures
    /// which fields are filterable and how they appear in the schema.
    /// </typeparam>
    public TraxGraphQLBuilder AddFilterType<TEntity, TFilter>()
        where TEntity : class
        where TFilter : FilterInputType<TEntity>
    {
        FilterTypeOverrides[typeof(TEntity)] = typeof(TFilter);
        return this;
    }

    /// <summary>
    /// Overrides the auto-generated <c>SortInputType</c> for a specific entity
    /// in the GraphQL schema. Use this when the default sort type exposes
    /// unwanted fields or when property names need remapping.
    /// </summary>
    /// <typeparam name="TEntity">The entity type marked with <c>[TraxQueryModel]</c>.</typeparam>
    /// <typeparam name="TSort">
    /// A <see cref="SortInputType{T}"/> subclass that configures
    /// which fields are sortable and how they appear in the schema.
    /// </typeparam>
    public TraxGraphQLBuilder AddSortType<TEntity, TSort>()
        where TEntity : class
        where TSort : SortInputType<TEntity>
    {
        SortTypeOverrides[typeof(TEntity)] = typeof(TSort);
        return this;
    }
}
