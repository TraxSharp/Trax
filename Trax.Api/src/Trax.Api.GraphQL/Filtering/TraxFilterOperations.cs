namespace Trax.Api.GraphQL.Filtering;

/// <summary>
/// Operation IDs for Trax's custom filter operations.
/// </summary>
/// <remarks>
/// HotChocolate reserves the low range (0..29, see its <c>DefaultFilterOperations</c>)
/// for built-in operators. Trax operations start at 1024 to stay clear of that range
/// and leave room for future HotChocolate additions.
/// </remarks>
internal static class TraxFilterOperations
{
    public const int IContains = 1024;
    public const int IEquals = 1025;
}
