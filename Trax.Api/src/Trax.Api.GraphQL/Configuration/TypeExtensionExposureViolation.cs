namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// One field the census rejected, carried from the type interceptor that found it to the
/// startup validator that reports it. The message is built where the violation is found, because
/// not every rejection is one of <see cref="ExposureViolation"/>'s three answers.
/// </summary>
internal sealed record TypeExtensionExposureViolation(string FieldPath, string Message);
