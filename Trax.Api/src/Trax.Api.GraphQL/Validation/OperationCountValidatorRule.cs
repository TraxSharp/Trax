using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Validation;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Validates that a single GraphQL request does not invoke more than the configured number of
/// operations. An operation is a field that does work: a root field, or a field under a
/// namespace such as <c>dispatch</c>, <c>discover</c>, <c>operations</c> or one of their nested
/// namespaces (<c>operations { deadLetters }</c>, a train's declared <c>Namespace</c>). A namespace
/// field is not counted itself; the selections under it are, so
/// <c>dispatch { a: t b: t c: t }</c> counts three, and so does
/// <c>x: dispatch { a: t } y: dispatch { b: t c: t }</c>.
/// </summary>
/// <remarks>
/// <para>
/// Aliased fields and batched operations both count: a payload with two operations each carrying
/// four operations has eight. Selections reached through fragment spreads and inline fragments
/// count as if written in place, and selections sharing a response path count once, because they
/// merge into one field at execution.
/// </para>
/// <para>
/// Guards against amplification / request-fanout denial of service where a single authenticated
/// caller issues hundreds of aliased mutation invocations in one HTTP round-trip. Each invocation
/// still goes through authorization, but even authorized fan-out can exhaust connection pools,
/// queue capacity, or downstream services. HotChocolate's own limits (<c>MaxAllowedFields</c>,
/// cost analysis) bound the size of a document, not the number of invocations in it, so this
/// rule is Trax's own.
/// </para>
/// </remarks>
internal sealed class OperationCountValidatorRule(int maxOperations) : IDocumentValidatorRule
{
    public bool IsCacheable => true;

    public ushort Priority => 0;

    public void Validate(DocumentValidatorContext context, DocumentNode document)
    {
        var walker = new Walker(context.Schema, document);
        var total = 0;
        foreach (var definition in document.Definitions)
        {
            if (definition is not OperationDefinitionNode op)
                continue;

            context.Schema.TryGetOperationType(op.Operation, out var rootType);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            walker.Collect(op.SelectionSet, rootType, prefix: "", paths);
            total += paths.Count;
            if (total <= maxOperations)
                continue;

            context.ReportError(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        $"The request exceeds the maximum allowed operations per request ({maxOperations})."
                    )
                    .SetCode("TRAX_TOO_MANY_OPERATIONS")
                    .Build()
            );
            return;
        }
    }

    /// <summary>
    /// Collects the response path of every operation in a selection set, descending through
    /// namespace fields and expanding fragment spreads and inline fragments. Each fragment's
    /// paths (relative to the fragment) are computed once and reused, so a fragment spread many
    /// times, directly or through other fragments, costs one expansion; a spread already on the
    /// expansion path (a cycle, which validation rejects on its own) contributes nothing.
    /// </summary>
    private sealed class Walker(ISchemaDefinition schema, DocumentNode document)
    {
        private readonly Dictionary<string, FragmentDefinitionNode> _fragments = document
            .Definitions.OfType<FragmentDefinitionNode>()
            .GroupBy(f => f.Name.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> _expanded = new(
            StringComparer.Ordinal
        );

        private readonly HashSet<string> _expanding = new(StringComparer.Ordinal);

        public void Collect(
            SelectionSetNode selectionSet,
            ITypeDefinition? parentType,
            string prefix,
            HashSet<string> into
        )
        {
            foreach (var selection in selectionSet.Selections)
            {
                switch (selection)
                {
                    case FieldNode field:
                        CollectField(field, parentType, prefix, into);
                        break;

                    case InlineFragmentNode inline:
                        Collect(
                            inline.SelectionSet,
                            TypeCondition(inline.TypeCondition) ?? parentType,
                            prefix,
                            into
                        );
                        break;

                    case FragmentSpreadNode spread:
                        var names = Expand(spread.Name.Value);
                        if (names is null)
                            break;
                        foreach (var name in names)
                            into.Add(prefix + name);
                        break;
                }
            }
        }

        private void CollectField(
            FieldNode field,
            ITypeDefinition? parentType,
            string prefix,
            HashSet<string> into
        )
        {
            var responseName = field.Alias?.Value ?? field.Name.Value;

            if (
                field.SelectionSet is not null
                && parentType is IComplexTypeDefinition complex
                && complex.Fields.TryGetField(field.Name.Value, out var definition)
                && NamespaceField.IsNamespace(definition)
            )
            {
                Collect(
                    field.SelectionSet,
                    definition.Type.NamedType(),
                    prefix + responseName + ".",
                    into
                );
                return;
            }

            into.Add(prefix + responseName);
        }

        private HashSet<string>? Expand(string fragmentName)
        {
            if (_expanded.TryGetValue(fragmentName, out var names))
                return names;

            if (!_fragments.TryGetValue(fragmentName, out var fragment))
                return null;
            if (!_expanding.Add(fragmentName))
                return null;

            names = new HashSet<string>(StringComparer.Ordinal);
            Collect(fragment.SelectionSet, TypeCondition(fragment.TypeCondition), "", names);
            _expanding.Remove(fragmentName);
            _expanded[fragmentName] = names;
            return names;
        }

        private ITypeDefinition? TypeCondition(NamedTypeNode? condition) =>
            condition is not null && schema.Types.TryGetType(condition.Name.Value, out var type)
                ? type
                : null;
    }
}
