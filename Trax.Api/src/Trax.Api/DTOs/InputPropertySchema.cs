namespace Trax.Api.DTOs;

/// <summary>
/// Describes a single property on a train's input type.
/// </summary>
public record InputPropertySchema(string Name, string TypeName, bool IsNullable);
