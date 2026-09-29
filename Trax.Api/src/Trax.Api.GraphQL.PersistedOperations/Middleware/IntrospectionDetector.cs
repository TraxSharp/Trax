using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Detects introspection requests so they can bypass persisted-operation
/// enforcement. A request is introspection when every top-level field in every
/// operation of its parsed document is <c>__schema</c>, <c>__type</c> or
/// <c>__typename</c>. The operation name plays no part: what the document selects
/// is the only thing that decides.
/// </summary>
internal static class IntrospectionDetector
{
    /// <summary>
    /// Full check against a parsed document. Returns true when every
    /// operation in the document selects only introspection fields.
    /// </summary>
    public static bool IsPureIntrospection(string document) =>
        IsPureIntrospection(GraphQLDocumentParser.TryParse(document));

    /// <summary>
    /// Full check against an already-parsed document. Returns true when every operation selects
    /// only introspection fields. A null document — which is what a document that did not parse
    /// becomes — is not introspection, so the rejection path handles it.
    /// </summary>
    public static bool IsPureIntrospection(DocumentNode? parsed)
    {
        if (parsed is null)
            return false;

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
