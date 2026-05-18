namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Thrown when strict response-shape validation (<see cref="ResponseStrictness.ThrowOnDrift"/>)
/// detects that the JSON returned by the server has fields the target POCO does not declare,
/// or vice versa. The intent is to catch silent drift between hand-written queries and their
/// response types on the very first response, not the hundredth bug report.
/// </summary>
public class GraphQLResponseShapeException : Exception
{
    public Type TargetType { get; }
    public IReadOnlyList<string> ExtraJsonFields { get; }
    public IReadOnlyList<string> MissingJsonFields { get; }

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
