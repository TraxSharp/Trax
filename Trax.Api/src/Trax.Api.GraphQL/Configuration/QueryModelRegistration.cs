using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Represents a discovered entity type marked with <see cref="TraxQueryModelAttribute"/>
/// and its owning DbContext type.
/// </summary>
public record QueryModelRegistration(
    Type EntityType,
    Type DbContextType,
    TraxQueryModelAttribute Attribute
);
