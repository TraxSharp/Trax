namespace Trax.Effect.Data.Models;

/// <summary>
/// Marker for a scalar-only projection of an entity owned by another schema.
/// </summary>
/// <remarks>
/// When a context reads an entity it does not own (a cross-schema read), it exposes that entity
/// through an <c>I{Entity}Reference : IEntityReference</c> interface that declares only scalar
/// columns, never navigation properties. Navigations on a cross-schema entity are
/// <see cref="Microsoft.EntityFrameworkCore.ModelBuilder"/>-ignored in the foreign context (the
/// <c>OnCrossSchemaModelCreating</c> convention), so a navigation declared on the reference interface
/// would compile everywhere and then throw at query time wherever it is traversed. Keeping reference
/// interfaces scalar-only makes that failure impossible.
/// </remarks>
public interface IEntityReference;
