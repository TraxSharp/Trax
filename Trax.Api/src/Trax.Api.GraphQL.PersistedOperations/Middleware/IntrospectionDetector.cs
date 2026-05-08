using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Detects introspection requests so they can bypass persisted-operation
/// enforcement. A request is "introspection" when:
/// <list type="bullet">
///   <item>The operation name is <c>IntrospectionQuery</c> (the de-facto convention).</item>
///   <item>OR every top-level field in every operation is <c>__schema</c> or <c>__type</c>.</item>
/// </list>
/// </summary>
internal static class IntrospectionDetector
{
    public const string DefaultIntrospectionOperationName = "IntrospectionQuery";

    /// <summary>
    /// Quick check by operation name only (avoids parsing the document).
    /// </summary>
    public static bool LooksLikeIntrospectionByName(string? operationName) =>
        string.Equals(operationName, DefaultIntrospectionOperationName, StringComparison.Ordinal);

    /// <summary>
    /// Full check against a parsed document. Returns true when every
    /// operation in the document selects only introspection fields.
    /// </summary>
    public static bool IsPureIntrospection(string document)
    {
        if (string.IsNullOrEmpty(document))
            return false;

        DocumentNode parsed;
        try
        {
            parsed = Utf8GraphQLParser.Parse(document);
        }
        catch
        {
            // Malformed documents are not introspection. Let the rejection
            // path handle them.
            return false;
        }

        var operations = parsed.Definitions.OfType<OperationDefinitionNode>().ToList();
        if (operations.Count == 0)
            return false;

        foreach (var op in operations)
        {
            foreach (var sel in op.SelectionSet.Selections)
            {
                if (sel is not FieldNode field)
                    return false;

                var name = field.Name.Value;
                if (
                    !string.Equals(name, "__schema", StringComparison.Ordinal)
                    && !string.Equals(name, "__type", StringComparison.Ordinal)
                    && !string.Equals(name, "__typename", StringComparison.Ordinal)
                )
                    return false;
            }
        }

        return true;
    }
}
