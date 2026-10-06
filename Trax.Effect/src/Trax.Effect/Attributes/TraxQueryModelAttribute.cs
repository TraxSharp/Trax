namespace Trax.Effect.Attributes;

/// <summary>
/// Exposes an EF Core entity as a typed GraphQL query field under <c>discover</c>
/// with optional cursor pagination, filtering, sorting, and projection.
/// </summary>
/// <remarks>
/// Place this attribute on entity classes that are exposed via a <c>DbSet&lt;T&gt;</c>
/// on a DbContext registered with <c>AddTraxGraphQL(g =&gt; g.AddDbContext&lt;TContext&gt;())</c>.
/// The generated field name is derived by pluralizing and camelCasing the class name
/// (e.g. <c>Player</c> → <c>players</c>), or overridden via <see cref="Name"/>.
///
/// Each feature (paging, filtering, sorting, projection) can be individually disabled
/// via the corresponding property. All default to <c>true</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxQueryModelAttribute : Attribute
{
    /// <summary>
    /// Overrides the auto-derived GraphQL field name.
    /// When null, the name is derived by pluralizing and camelCasing the class name
    /// (e.g. <c>Player</c> → <c>players</c>, <c>MatchResult</c> → <c>matchResults</c>).
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

    /// <summary>
    /// Enables cursor-based pagination (Relay Connection spec) on the query field.
    /// When <c>true</c>, the field returns a Connection type with <c>nodes</c>,
    /// <c>edges</c>, and <c>pageInfo</c>.
    /// </summary>
    public bool Paging { get; init; } = true;

    /// <summary>
    /// Enables filtering on the query field. When <c>true</c>, clients can use
    /// a <c>where</c> argument to filter results by entity properties.
    /// </summary>
    public bool Filtering { get; init; } = true;

    /// <summary>
    /// Enables sorting on the query field. When <c>true</c>, clients can use
    /// an <c>order</c> argument to sort results by entity properties.
    /// </summary>
    public bool Sorting { get; init; } = true;

    /// <summary>
    /// Enables field projection on the query field. When <c>true</c>,
    /// only the columns requested by the client are selected from the database.
    /// </summary>
    public bool Projection { get; init; } = true;

    /// <summary>
    /// Controls how fields are bound on the generated GraphQL ObjectType.
    /// When <see cref="FieldBindingBehavior.Explicit"/>, only properties with
    /// <see cref="System.ComponentModel.DataAnnotations.Schema.ColumnAttribute"/>
    /// are exposed; <c>[NotMapped]</c> properties, methods, and other public members are excluded.
    /// Defaults to <see cref="FieldBindingBehavior.Implicit"/> (all public properties exposed).
    /// </summary>
    public FieldBindingBehavior BindFields { get; init; } = FieldBindingBehavior.Implicit;

    /// <summary>
    /// Groups this field under a sub-namespace in the GraphQL schema.
    /// When set, the field appears under <c>discover { namespace { field } }</c>
    /// instead of directly under <c>discover { field }</c>.
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Exposes the entity in GraphQL using the property set declared by the supplied
    /// interface (typically a scalar-only <c>I{Model}Reference</c> projection used for
    /// cross-schema or cross-domain reads). The entity class must implement the interface
    /// implicitly. Only properties declared on the interface (and its inherited interfaces)
    /// appear in the schema; filter and sort input types are constrained to the same set
    /// unless a custom <c>FilterInputType</c> / <c>SortInputType</c> is supplied via
    /// <c>AddFilterType</c> / <c>AddSortType</c>.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="BindFields"/> = <see cref="FieldBindingBehavior.Explicit"/>.
    /// Both restrict the exposed property set, so combining them is rejected at build time.
    /// </remarks>
    public Type? ExposeAs { get; init; }
}
