using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Detects a request that selects <i>only</i> this package's own management surface, which
/// bypasses persisted-operation enforcement because persisting the upload mutation by id would
/// be a chicken-and-egg.
/// </summary>
/// <remarks>
/// The carve-out sits above the <c>RequirePersisted</c> check, so nothing downstream can narrow
/// it and no host option can turn it off. That makes "is this the management surface" a question
/// that has to be answered from the document's structure rather than its text: an alias is the
/// caller's choice of response key, a string argument is data, and a comment is not in the
/// document at all. Only the field actually being selected counts.
/// <para>
/// Mirrors <see cref="IntrospectionDetector.IsPureIntrospection(DocumentNode?)"/>: every
/// operation, and only these fields. A document mixing the management surface with anything else
/// is not a management operation — reaching something else is the reason to mix, not a use case.
/// </para>
/// </remarks>
internal static class ManagementOperationDetector
{
    private const string OperationsField = "operations";
    private const string PersistedOperationsField = "persistedOperations";

    /// <summary>
    /// Whether every operation in the document selects <c>operations</c> at the root and nothing
    /// but <c>persistedOperations</c> beneath it. A null document — which is what a document that
    /// did not parse becomes — is not a management operation, so the rejection path runs.
    /// </summary>
    public static bool IsManagementOperation(DocumentNode? document)
    {
        if (document is null)
            return false;

        var operations = document.Definitions.OfType<OperationDefinitionNode>().ToList();
        if (operations.Count == 0)
            return false;

        return operations.All(operation =>
            SelectsOnly(operation.SelectionSet, OperationsField, SelectsOnlyPersistedOperations)
        );
    }

    private static bool SelectsOnlyPersistedOperations(FieldNode operationsField) =>
        operationsField.SelectionSet is not null
        && SelectsOnly(operationsField.SelectionSet, PersistedOperationsField, _ => true);

    /// <summary>
    /// Whether every selection in the set is the named field and satisfies <paramref name="inner"/>.
    /// <c>__typename</c> is tolerated: it reveals nothing and asks for nothing.
    /// </summary>
    /// <remarks>
    /// Compares <c>Name.Value</c>, the field being selected, never <c>Alias</c>. A fragment spread
    /// or inline fragment is not a field, so it cannot be shown to stay inside the carve-out and
    /// disqualifies the document.
    /// </remarks>
    private static bool SelectsOnly(
        SelectionSetNode selectionSet,
        string fieldName,
        Func<FieldNode, bool> inner
    )
    {
        foreach (var selection in selectionSet.Selections)
        {
            if (selection is not FieldNode field)
                return false;

            if (string.Equals(field.Name.Value, "__typename", StringComparison.Ordinal))
                continue;

            if (!string.Equals(field.Name.Value, fieldName, StringComparison.Ordinal))
                return false;

            if (!inner(field))
                return false;
        }

        return true;
    }
}
