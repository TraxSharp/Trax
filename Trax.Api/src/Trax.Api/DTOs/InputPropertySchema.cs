namespace Trax.Api.DTOs;

/// <summary>
/// Describes a single property on a train's input type.
/// </summary>
/// <param name="Name">
/// The JSON name the input reader expects: the property name under the system JSON naming
/// policy, or its <c>[JsonPropertyName]</c> when it has one.
/// </param>
/// <param name="TypeName">A readable name for the property's CLR type.</param>
/// <param name="IsNullable">Whether the property accepts <c>null</c>.</param>
/// <param name="EnumValues">
/// The values the property accepts when its type is an enum (or a nullable enum), spelled the way
/// the input reader expects them; <c>null</c> for any other type.
/// </param>
public record InputPropertySchema(
    string Name,
    string TypeName,
    bool IsNullable,
    IReadOnlyList<string>? EnumValues = null
);
