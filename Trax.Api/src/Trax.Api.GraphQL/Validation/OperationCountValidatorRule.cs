using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Validation;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Validates that a single GraphQL request does not submit more than the
/// configured number of top-level selections. Aliased fields and batched
/// operations both count: a request with <c>a: foo b: foo c: foo</c> has three,
/// and a payload with two operations each carrying four root fields has eight.
/// Selections reached through fragment spreads and inline fragments count as if
/// written in place, and selections sharing a response name count once, because
/// they merge into one field at execution.
/// </summary>
/// <remarks>
/// Guards against amplification / request-fanout denial of service where a
/// single authenticated caller issues hundreds of aliased mutation invocations
/// in one HTTP round-trip. Each invocation still goes through
/// authorization, but even authorized fan-out can exhaust connection pools,
/// queue capacity, or downstream services.
/// </remarks>
internal sealed class OperationCountValidatorRule(int maxOperations) : IDocumentValidatorRule
{
    public bool IsCacheable => true;

    public ushort Priority => 0;

    public void Validate(DocumentValidatorContext context, DocumentNode document)
    {
        var fragments = new Dictionary<string, FragmentDefinitionNode>(StringComparer.Ordinal);
        foreach (var definition in document.Definitions)
            if (definition is FragmentDefinitionNode fragment)
                fragments.TryAdd(fragment.Name.Value, fragment);

        var expanded = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var total = 0;
        foreach (var definition in document.Definitions)
        {
            if (definition is not OperationDefinitionNode op)
                continue;

            var responseNames = new HashSet<string>(StringComparer.Ordinal);
            CollectResponseNames(op.SelectionSet, fragments, expanded, [], responseNames);
            total += responseNames.Count;
            if (total <= maxOperations)
                continue;

            context.ReportError(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"The request exceeds the maximum allowed selections per request ({maxOperations})."
                    )
                    .SetCode("TRAX_TOO_MANY_OPERATIONS")
                    .Build()
            );
            return;
        }
    }

    /// <summary>
    /// Adds the response name of every field in <paramref name="selectionSet"/>, expanding
    /// fragment spreads and inline fragments. Each fragment's names are computed once and
    /// reused, so a fragment spread many times, directly or through other fragments, costs
    /// one expansion; a spread already on the expansion path (a cycle, which validation
    /// rejects on its own) contributes nothing.
    /// </summary>
    private static void CollectResponseNames(
        SelectionSetNode selectionSet,
        Dictionary<string, FragmentDefinitionNode> fragments,
        Dictionary<string, HashSet<string>> expanded,
        HashSet<string> expanding,
        HashSet<string> into
    )
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    into.Add(field.Alias?.Value ?? field.Name.Value);
                    break;

                case InlineFragmentNode inline:
                    CollectResponseNames(inline.SelectionSet, fragments, expanded, expanding, into);
                    break;

                case FragmentSpreadNode spread:
                    var name = spread.Name.Value;
                    if (!expanded.TryGetValue(name, out var names))
                    {
                        if (!fragments.TryGetValue(name, out var fragment) || !expanding.Add(name))
                            break;

                        names = new HashSet<string>(StringComparer.Ordinal);
                        CollectResponseNames(
                            fragment.SelectionSet,
                            fragments,
                            expanded,
                            expanding,
                            names
                        );
                        expanding.Remove(name);
                        expanded[name] = names;
                    }
                    into.UnionWith(names);
                    break;
            }
        }
    }
}
