using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Represents a discovered entity type marked with <see cref="TraxQueryModelAttribute"/>
/// and its owning DbContext type. <see cref="AuthorizeAttributes"/> captures every
/// <see cref="TraxAuthorizeAttribute"/> applied to the entity (including those inherited
/// from base classes / interfaces) so the type module can attach the <c>@authorize</c>
/// directive at <c>ObjectType</c> level for transitive enforcement.
/// </summary>
public record QueryModelRegistration(
    Type EntityType,
    Type DbContextType,
    TraxQueryModelAttribute Attribute,
    Type? FilterInputType = null,
    Type? SortInputType = null,
    IReadOnlyList<TraxAuthorizeAttribute>? AuthorizeAttributes = null
)
{
    public IReadOnlyList<TraxAuthorizeAttribute> AuthorizeAttributes { get; init; } =
        AuthorizeAttributes ?? Array.Empty<TraxAuthorizeAttribute>();
}
