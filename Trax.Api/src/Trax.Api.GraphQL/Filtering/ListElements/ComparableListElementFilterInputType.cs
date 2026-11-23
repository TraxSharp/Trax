using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>Comparable scalar elements (numeric, date, Guid), without <c>neq</c>.</summary>
internal sealed class ComparableListElementFilterInputType<T>
    : ComparableOperationFilterInputType<T>
    where T : struct
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(T)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
