using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Represents a discovered entity type marked with <see cref="TraxQueryModelAttribute"/>
/// and its owning DbContext type. <see cref="AuthorizeAttributes"/> captures every
/// <see cref="TraxAuthorizeAttribute"/> applied to the entity (including those inherited
/// from base classes / interfaces) so the type module can attach the <c>@authorize</c>
/// directive at <c>ObjectType</c> level for transitive enforcement.
/// <para>
/// <see cref="AllowAnonymous"/> is the inverse opt-in: when true, the type module
/// skips every <c>@authorize</c> emission for this entity and the model-exposure
/// warning service excludes it from the ungated-surface count. Mutually exclusive
/// with <see cref="AuthorizeAttributes"/>; the conflict is caught at
/// <c>TraxGraphQLBuilder.Build()</c>.
/// </para>
/// </summary>
public record QueryModelRegistration
{
    /// <summary>
    /// Creates a registration. <see cref="TraxGraphQLBuilder.TraxGraphQLBuilder"/> builds these from the
    /// discovered entities; hosts read them from <see cref="GraphQLConfiguration.ModelRegistrations"/>.
    /// </summary>
    internal QueryModelRegistration(
        Type EntityType,
        Type DbContextType,
        TraxQueryModelAttribute Attribute,
        Type? FilterInputType = null,
        Type? SortInputType = null,
        IReadOnlyList<TraxAuthorizeAttribute>? AuthorizeAttributes = null,
        bool AllowAnonymous = false
    )
    {
        this.EntityType = EntityType;
        this.DbContextType = DbContextType;
        this.Attribute = Attribute;
        this.FilterInputType = FilterInputType;
        this.SortInputType = SortInputType;
        this.AuthorizeAttributes = AuthorizeAttributes ?? Array.Empty<TraxAuthorizeAttribute>();
        this.AllowAnonymous = AllowAnonymous;
    }

    /// <summary>The entity type marked with <c>[TraxQueryModel]</c>.</summary>
    public Type EntityType { get; internal init; }

    /// <summary>The DbContext that owns the entity's <c>DbSet</c>.</summary>
    public Type DbContextType { get; internal init; }

    /// <summary>The <c>[TraxQueryModel]</c> attribute on the entity.</summary>
    public TraxQueryModelAttribute Attribute { get; internal init; }

    /// <summary>The filter input type registered with <c>AddFilterType</c>, or <c>null</c> for the inferred one.</summary>
    public Type? FilterInputType { get; internal init; }

    /// <summary>The sort input type registered with <c>AddSortType</c>, or <c>null</c> for the inferred one.</summary>
    public Type? SortInputType { get; internal init; }

    /// <summary>
    /// Every <c>[TraxAuthorize]</c> on the entity, including those inherited from base classes and
    /// interfaces. Never <c>null</c>: empty when the entity carries none.
    /// </summary>
    public IReadOnlyList<TraxAuthorizeAttribute> AuthorizeAttributes { get; internal init; }

    /// <summary>Whether the entity carries <c>[TraxAllowAnonymous]</c>, so no <c>@authorize</c> is emitted for it.</summary>
    public bool AllowAnonymous { get; internal init; }
}
