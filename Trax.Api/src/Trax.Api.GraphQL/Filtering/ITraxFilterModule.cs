using HotChocolate.Data.Filters;

namespace Trax.Api.GraphQL.Filtering;

/// <summary>
/// A self-contained contribution to Trax's filter convention. Each module registers
/// one cohesive set of filter operations onto the HotChocolate
/// <see cref="IFilterConventionDescriptor"/>: the operation IDs, their GraphQL field
/// names, the input-type fields, and the queryable expression handlers that translate
/// them.
/// </summary>
/// <remarks>
/// Modules are additive and convention-level, so the operations they add appear on
/// every string filter input in the schema, including <c>ExposeAs</c>-projected inputs
/// and custom <c>AddFilterType</c> overrides. Add a new filter capability by writing a
/// module and exposing it through a method on
/// <see cref="Configuration.TraxFilterBuilder.TraxFilterBuilder"/>.
/// </remarks>
public interface ITraxFilterModule
{
    /// <summary>
    /// Applies this module's operations to the filter convention. Called once during
    /// schema construction, inside the <c>AddFiltering(convention =&gt; ...)</c> callback.
    /// </summary>
    void Apply(IFilterConventionDescriptor descriptor);
}
