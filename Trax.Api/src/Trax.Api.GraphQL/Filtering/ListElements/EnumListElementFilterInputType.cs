using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering.ListElements;

/// <summary>Enum elements, without <c>neq</c>.</summary>
internal sealed class EnumListElementFilterInputType<TEnum> : EnumOperationFilterInputType<TEnum>
{
    protected override void Configure(IFilterInputTypeDescriptor descriptor)
    {
        base.Configure(descriptor);
        descriptor.Name(ListElementFilterNaming.ElementTypeName(typeof(TEnum)));
        descriptor.Operation(DefaultFilterOperations.NotEquals).Ignore();
    }
}
