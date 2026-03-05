namespace Trax.Effect.Attributes;

/// <summary>
/// Exposes a train as a typed GraphQL query field under <c>discover</c>.
/// Query trains always execute synchronously via <c>RunAsync</c> on the current server.
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. The generated field name is derived
/// by stripping the <c>I</c> prefix and <c>Train</c> suffix from the service type name
/// (e.g. <c>ILookupPlayerTrain</c> → <c>lookupPlayer</c>), or overridden via <see cref="Name"/>.
///
/// Trains without this attribute (or <see cref="TraxMutationAttribute"/>) are not exposed
/// as GraphQL endpoints. A train cannot have both attributes.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxQueryAttribute : Attribute
{
    /// <summary>
    /// Overrides the auto-derived GraphQL field name.
    /// When null, the name is derived by stripping the "I" prefix and "Train" suffix
    /// from the service type name (e.g. <c>ILookupPlayerTrain</c> → <c>lookupPlayer</c>).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// A human-readable description that appears in the GraphQL schema documentation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Marks the generated field as deprecated in the GraphQL schema.
    /// Clients see a deprecation warning during introspection.
    /// </summary>
    public string? DeprecationReason { get; init; }
}
