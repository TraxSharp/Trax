using System.Security.Cryptography;
using System.Text;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.ShapeDiff;

/// <summary>
/// Computes a canonicalized structural hash of the response shape produced
/// by a GraphQL operation. Two documents that produce byte-compatible JSON
/// shapes hash to the same fingerprint; documents that would change a
/// shipped client's deserialization hash differently.
/// </summary>
/// <remarks>
/// Algorithm:
/// <list type="number">
///   <item>Parse via <see cref="Utf8GraphQLParser.Parse(string)"/>.</item>
///   <item>Locate the executable operation (by name, or the only operation).</item>
///   <item>Inline fragment spreads with their type conditions.</item>
///   <item>For each field emit <c>(parentType, responseKey, fieldName, hasInclude, hasSkip)</c>.</item>
///   <item>Sort sibling tuples by response key (canonical ordering).</item>
///   <item>Recurse, indenting by depth.</item>
///   <item>SHA-256 hex of the canonical string.</item>
/// </list>
/// </remarks>
public static class ShapeFingerprintComputer
{
    /// <summary>
    /// Compute the fingerprint for the named operation, or the single
    /// operation when <paramref name="operationName"/> is null.
    /// </summary>
    public static string Compute(string document, string? operationName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(document);

        var parsed = Utf8GraphQLParser.Parse(document);
        var canonical = BuildCanonicalString(parsed, operationName);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Builds the canonical string the fingerprint hashes. Exposed for tests
    /// and diagnostics so failures can be inspected without re-deriving.
    /// </summary>
    internal static string BuildCanonicalString(DocumentNode document, string? operationName)
    {
        var operations = document.Definitions.OfType<OperationDefinitionNode>().ToList();

        if (operations.Count == 0)
            throw new InvalidOperationException(
                "Document contains no executable operation. Persisted operations require at least one query, mutation, or subscription."
            );

        var op = operationName is null
            ? (
                operations.Count == 1
                    ? operations[0]
                    : throw new InvalidOperationException(
                        "Document contains multiple operations; pass operationName to disambiguate."
                    )
            )
            : operations.FirstOrDefault(o =>
                string.Equals(o.Name?.Value, operationName, StringComparison.Ordinal)
            )
                ?? throw new InvalidOperationException(
                    $"Document does not contain an operation named '{operationName}'."
                );

        var fragments = document
            .Definitions.OfType<FragmentDefinitionNode>()
            .ToDictionary(f => f.Name.Value, f => f, StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append("op:").Append(op.Operation).Append('\n');

        WriteSelectionSet(
            sb,
            op.SelectionSet,
            parentType: null,
            depth: 0,
            fragments,
            visitedFragments: new HashSet<string>(StringComparer.Ordinal)
        );

        return sb.ToString();
    }

    private static void WriteSelectionSet(
        StringBuilder sb,
        SelectionSetNode selectionSet,
        string? parentType,
        int depth,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        HashSet<string> visitedFragments
    )
    {
        var entries = new List<CanonicalEntry>();
        FlattenSelections(selectionSet, parentType, fragments, visitedFragments, entries);

        // Canonical ordering: sort by (parentType, responseKey, fieldName).
        entries.Sort(CanonicalEntry.Comparer);

        var indent = new string(' ', depth * 2);
        foreach (var entry in entries)
        {
            sb.Append(indent)
                .Append(entry.ParentType ?? "*")
                .Append('|')
                .Append(entry.ResponseKey)
                .Append('|')
                .Append(entry.FieldName)
                .Append('|')
                .Append(entry.HasInclude ? 'I' : '-')
                .Append(entry.HasSkip ? 'S' : '-')
                .Append('\n');

            if (entry.SelectionSet is not null)
                WriteSelectionSet(
                    sb,
                    entry.SelectionSet,
                    parentType: null, // unknown without schema; only inline/spread type conditions set it.
                    depth + 1,
                    fragments,
                    visitedFragments
                );
        }
    }

    private static void FlattenSelections(
        SelectionSetNode selectionSet,
        string? parentType,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        HashSet<string> visitedFragments,
        List<CanonicalEntry> sink
    )
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    sink.Add(BuildEntry(field, parentType));
                    break;

                case InlineFragmentNode inline:
                {
                    var nestedType = inline.TypeCondition?.Name.Value ?? parentType;
                    FlattenSelections(
                        inline.SelectionSet,
                        nestedType,
                        fragments,
                        visitedFragments,
                        sink
                    );
                    break;
                }

                case FragmentSpreadNode spread:
                {
                    var name = spread.Name.Value;
                    if (!visitedFragments.Add(name))
                        continue; // recursion guard

                    if (!fragments.TryGetValue(name, out var def))
                        continue; // missing fragment: skip; parser would have warned

                    var nestedType = def.TypeCondition.Name.Value;
                    FlattenSelections(
                        def.SelectionSet,
                        nestedType,
                        fragments,
                        visitedFragments,
                        sink
                    );

                    visitedFragments.Remove(name);
                    break;
                }
            }
        }
    }

    private static CanonicalEntry BuildEntry(FieldNode field, string? parentType)
    {
        var responseKey = field.Alias?.Value ?? field.Name.Value;
        var hasInclude = field.Directives.Any(d =>
            string.Equals(d.Name.Value, "include", StringComparison.Ordinal)
        );
        var hasSkip = field.Directives.Any(d =>
            string.Equals(d.Name.Value, "skip", StringComparison.Ordinal)
        );

        return new CanonicalEntry(
            parentType,
            responseKey,
            field.Name.Value,
            hasInclude,
            hasSkip,
            field.SelectionSet
        );
    }

    private sealed record CanonicalEntry(
        string? ParentType,
        string ResponseKey,
        string FieldName,
        bool HasInclude,
        bool HasSkip,
        SelectionSetNode? SelectionSet
    )
    {
        // List<T>.Sort(Comparison<T>) never invokes the comparer with null
        // entries (List<T> doesn't carry nulls in the producer here), so the
        // delegate form is enough and keeps the surface honest about the
        // values it actually compares.
        public static readonly Comparison<CanonicalEntry> Comparer = static (x, y) =>
        {
            var parent = string.CompareOrdinal(
                x.ParentType ?? string.Empty,
                y.ParentType ?? string.Empty
            );
            if (parent != 0)
                return parent;

            var key = string.CompareOrdinal(x.ResponseKey, y.ResponseKey);
            if (key != 0)
                return key;

            return string.CompareOrdinal(x.FieldName, y.FieldName);
        };
    }
}
