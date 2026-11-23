using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// The filter input for a <c>[TraxQueryModel]</c> entity, built from its exposed field set.
/// Bound to the entity in the filter convention, so the same input is used on the model's own
/// entry field and wherever another model's filter reaches it through a navigation.
/// </summary>
internal sealed class QueryModelFilterInputType<TEntity> : FilterInputType<TEntity>
{
    protected override void Configure(IFilterInputTypeDescriptor<TEntity> descriptor) =>
        QueryModelFieldSet.BindFilterFields(descriptor);
}
