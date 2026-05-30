using HotChocolate.Data.Filters.Expressions;
using Trax.Api.GraphQL.Filtering.Operations;

namespace Trax.Api.GraphQL.Filtering;

/// <summary>
/// Adds the case-insensitive string expression handlers to the queryable filter
/// provider without replacing it, so they compose with HotChocolate's built-in
/// operation handlers.
/// </summary>
internal sealed class TraxCaseInsensitiveFilterProviderExtension : QueryableFilterProviderExtension
{
    public TraxCaseInsensitiveFilterProviderExtension()
        : base(descriptor =>
            descriptor
                .AddFieldHandler<QueryableStringIContainsHandler>()
                .AddFieldHandler<QueryableStringIEqualsHandler>()
        ) { }
}
