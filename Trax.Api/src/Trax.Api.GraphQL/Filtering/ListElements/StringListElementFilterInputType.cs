using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>String elements, without <c>neq</c>.</summary>
internal sealed class StringListElementFilterInputType : StringOperationFilterInputType
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(string)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
