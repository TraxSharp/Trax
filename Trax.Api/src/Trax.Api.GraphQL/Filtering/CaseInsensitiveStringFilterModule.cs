using HotChocolate.Data.Filters;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Filtering;

/// <summary>
/// Adds case-insensitive string filter operations to the schema:
/// <list type="bullet">
///   <item><c>icontains</c> — case-insensitive substring match.</item>
///   <item><c>ieq</c> — case-insensitive equality.</item>
/// </list>
/// Both fold with SQL <c>lower()</c> rather than <c>ILIKE</c>, so they work across every
/// provider (Npgsql, SQL Server, SQLite, InMemory) and stay sargable against a
/// <c>lower(col)</c> expression index. The existing case-sensitive <c>contains</c> /
/// <c>eq</c> operations are untouched; callers opt in per query by choosing the operator.
/// </summary>
public sealed class CaseInsensitiveStringFilterModule : ITraxFilterModule
{
    public void Apply(IFilterConventionDescriptor descriptor)
    {
        descriptor.Operation(TraxFilterOperations.IContains).Name("icontains");
        descriptor.Operation(TraxFilterOperations.IEquals).Name("ieq");

        descriptor.Configure<StringOperationFilterInputType>(input =>
        {
            input.Operation(TraxFilterOperations.IContains).Type<StringType>();
            input.Operation(TraxFilterOperations.IEquals).Type<StringType>();
        });

        descriptor.AddProviderExtension<TraxCaseInsensitiveFilterProviderExtension>();
    }
}
