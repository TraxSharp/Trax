using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>
/// The list filter for a scalar collection. Identical to
/// <see cref="ListFilterInputType{T}"/> apart from keeping HotChocolate's stock name
/// while pointing <c>some</c>/<c>all</c>/<c>none</c> at the restricted element type.
/// </summary>
/// <typeparam name="TElement">The collection's element runtime type.</typeparam>
/// <typeparam name="TElementFilter">The restricted element filter input.</typeparam>
internal sealed class ListElementFilterInputType<TElement, TElementFilter>
    : ListFilterInputType<TElementFilter>
    where TElementFilter : FilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ListTypeName(typeof(TElement)));
    }
}
