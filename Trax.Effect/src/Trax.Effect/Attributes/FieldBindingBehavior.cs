namespace Trax.Effect.Attributes;

/// <summary>
/// Controls how fields are bound when generating the GraphQL ObjectType
/// for an entity marked with <see cref="TraxQueryModelAttribute"/>.
/// </summary>
public enum FieldBindingBehavior
{
    /// <summary>
    /// All public properties and methods are exposed as GraphQL fields (default HotChocolate behavior).
    /// </summary>
    Implicit = 0,

    /// <summary>
    /// Only properties decorated with <see cref="System.ComponentModel.DataAnnotations.Schema.ColumnAttribute"/>
    /// are exposed. Properties with <c>[NotMapped]</c>, methods, and non-column public members are excluded.
    /// </summary>
    Explicit = 1,
}
