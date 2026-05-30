using Trax.Api.GraphQL.Filtering;

namespace Trax.Api.GraphQL.Configuration.TraxFilterBuilder;

public partial class TraxFilterBuilder
{
    /// <summary>
    /// Returns the filter modules accumulated by the builder, in registration order.
    /// </summary>
    internal IReadOnlyList<ITraxFilterModule> Build() => Modules;
}
