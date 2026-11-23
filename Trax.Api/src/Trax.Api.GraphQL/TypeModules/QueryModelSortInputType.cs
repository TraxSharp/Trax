using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// The sort input for a <c>[TraxQueryModel]</c> entity, built from its exposed field set. See
/// <see cref="QueryModelFilterInputType{TEntity}"/>.
/// </summary>
internal sealed class QueryModelSortInputType<TEntity> : SortInputType<TEntity>
{
    protected override void Configure(ISortInputTypeDescriptor<TEntity> descriptor) =>
        QueryModelFieldSet.BindSortFields(descriptor);
}
