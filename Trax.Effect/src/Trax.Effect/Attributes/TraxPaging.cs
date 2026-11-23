namespace Trax.Effect.Attributes;

/// <summary>
/// How a <see cref="TraxQueryModelAttribute"/> list is paged.
/// </summary>
public enum TraxPaging
{
    /// <summary>
    /// Whatever the schema's default is: <see cref="Cursor"/>, unless the host sets another with
    /// <c>DefaultPaging</c> on its GraphQL builder.
    /// </summary>
    Default,

    /// <summary>No paging: the field returns a plain list (<c>[Entity!]!</c>).</summary>
    None,

    /// <summary>
    /// Relay cursor paging: <c>first</c>/<c>after</c>/<c>last</c>/<c>before</c> in, a connection
    /// with <c>nodes</c>, <c>edges</c>, <c>pageInfo</c> and <c>totalCount</c> out.
    /// </summary>
    Cursor,

    /// <summary>
    /// Offset paging: <c>skip</c>/<c>take</c> in, a collection segment with <c>items</c>,
    /// <c>pageInfo</c> and <c>totalCount</c> out.
    /// </summary>
    Offset,
}
