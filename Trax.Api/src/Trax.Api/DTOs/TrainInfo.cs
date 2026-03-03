namespace Trax.Api.DTOs;

/// <summary>
/// A train available in the system, including its input schema for API consumers.
/// </summary>
public record TrainInfo(
    string ServiceTypeName,
    string ImplementationTypeName,
    string InputTypeName,
    string OutputTypeName,
    string Lifetime,
    IReadOnlyList<InputPropertySchema> InputSchema,
    IReadOnlyList<string> RequiredPolicies,
    IReadOnlyList<string> RequiredRoles
);
