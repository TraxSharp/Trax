using GraphQL.Types;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// A graphql-dotnet <see cref="ScalarGraphType"/> implementation that accepts any value
/// shape. Used to back custom scalars discovered in an introspected schema (e.g.,
/// HotChocolate's <c>Any</c>, <c>DateTime</c>, <c>Uuid</c>, <c>JSON</c>) for client-side
/// query validation.
///
/// The client validator only cares that fields and types exist and that operations are
/// well-formed. <see cref="ScalarGraphType.ParseLiteral"/> falls back on the base class's
/// reasonable default (delegates through <see cref="CanParseLiteral"/>), so the only
/// override we need is <see cref="ParseValue"/>, which graphql-dotnet declares abstract.
/// Server-side execution applies the real scalar semantics.
/// </summary>
internal sealed class PermissiveScalarGraphType : ScalarGraphType
{
    public PermissiveScalarGraphType(string name)
    {
        // IntrospectingSchemaProvider filters on `type.Name is { Length: > 0 }` before
        // constructing, so we trust the caller here. No defensive null/empty validation.
        Name = name;
    }

    public override object? ParseValue(object? value) => value;
}
