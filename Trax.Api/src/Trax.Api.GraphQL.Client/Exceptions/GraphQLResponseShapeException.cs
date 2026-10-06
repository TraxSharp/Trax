namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Thrown when strict response-shape validation (<see cref="ResponseStrictness.ThrowOnDrift"/>)
/// detects that the JSON returned by the server has fields the target POCO does not declare,
/// or vice versa. The intent is to catch silent drift between hand-written queries and their
/// response types on the very first response, not the hundredth bug report.
/// </summary>
public class GraphQLResponseShapeException : Exception
{
    /// <summary>The response type the JSON was checked against.</summary>
    public Type TargetType { get; }

    /// <summary>Fields in the response that the response type does not declare.</summary>
    public IReadOnlyList<string> ExtraJsonFields { get; }

    /// <summary>Properties of the response type that the response did not include.</summary>
    public IReadOnlyList<string> MissingJsonFields { get; }

    /// <summary>Creates the exception; the message lists both sets of fields.</summary>
    /// <param name="targetType">The response type.</param>
    /// <param name="extraJsonFields">Fields in the response the type does not declare.</param>
    /// <param name="missingJsonFields">Properties of the type missing from the response.</param>
    public GraphQLResponseShapeException(
        Type targetType,
        IReadOnlyList<string> extraJsonFields,
        IReadOnlyList<string> missingJsonFields
    )
        : base(BuildMessage(targetType, extraJsonFields, missingJsonFields))
    {
        TargetType = targetType;
        ExtraJsonFields = extraJsonFields;
        MissingJsonFields = missingJsonFields;
    }

    private static string BuildMessage(
        Type targetType,
        IReadOnlyList<string> extra,
        IReadOnlyList<string> missing
    )
    {
        var parts = new List<string>();
        if (extra.Count > 0)
            parts.Add($"extra fields in response not on POCO: {string.Join(", ", extra)}");
        if (missing.Count > 0)
            parts.Add($"fields declared on POCO not in response: {string.Join(", ", missing)}");

        return $"Response shape does not match {targetType.Name}: {string.Join("; ", parts)}.";
    }
}
