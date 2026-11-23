using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>Boolean elements, without <c>neq</c>.</summary>
internal sealed class BooleanListElementFilterInputType : BooleanOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(bool)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
