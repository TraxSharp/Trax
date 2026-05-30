using Trax.Api.GraphQL.Filtering;

namespace Trax.Api.GraphQL.Configuration.TraxFilterBuilder;

/// <summary>
/// Configures Trax's filter convention. Today it exposes opt-in case-insensitive string
/// operations; it is the extension point for future filter operators. Reached via
/// <see cref="TraxGraphQLBuilder.TraxGraphQLBuilder.ConfigureFiltering"/>.
/// </summary>
public partial class TraxFilterBuilder
{
    internal List<ITraxFilterModule> Modules { get; } = [];
}
