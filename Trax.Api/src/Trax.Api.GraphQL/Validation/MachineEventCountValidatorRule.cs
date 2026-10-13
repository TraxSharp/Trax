using HotChocolate;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Validation;

namespace Trax.Api.GraphQL.Validation;

/// <summary>
/// Marks a field under the <c>machines</c> root that writes an instance: <c>start</c>, <c>discard</c> and every
/// event. <see cref="MachineEventCountValidatorRule"/> refuses an operation that selects more than one.
/// </summary>
internal sealed class MachineWriteField
{
    private static readonly MachineWriteField Marker = new();

    private MachineWriteField() { }

    /// <summary>Marks a field built through a descriptor as a machine write.</summary>
    internal static IObjectFieldDescriptor Mark(IObjectFieldDescriptor descriptor)
    {
        descriptor.Extend().Configuration.Features.Set(Marker);
        return descriptor;
    }

    /// <summary>True when <paramref name="field"/> was marked as a machine write.</summary>
    internal static bool IsMachineWrite(IOutputFieldDefinition field) =>
        field is IFeatureProvider provider
        && provider.Features.Get<MachineWriteField>() is not null;
}

/// <summary>
/// Refuses an operation that selects more than one machine write (<c>start</c>, <c>discard</c> or an event), on one
/// machine or across several, before any of them runs. HotChocolate resolves the fields under a namespace in
/// parallel, so two writes in one operation would race each other on the same instance, or commit one and refuse the
/// other with no order a client could rely on. Selections reached through fragments count as if written in place,
/// and selections sharing a response path count once, because they merge into one field.
/// See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal sealed class MachineEventCountValidatorRule : IDocumentValidatorRule
{
    /// <summary>The error code of the refusal.</summary>
    public const string Code = "TRAX_ONE_MACHINE_EVENT";

    public bool IsCacheable => true;

    public ushort Priority => 0;

    public void Validate(DocumentValidatorContext context, DocumentNode document)
    {
        var fragments = document
            .Definitions.OfType<FragmentDefinitionNode>()
            .GroupBy(f => f.Name.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var op in document.Definitions.OfType<OperationDefinitionNode>())
        {
            if (op.Operation != OperationType.Mutation)
                continue;
            if (!context.Schema.TryGetOperationType(op.Operation, out var root))
                continue;

            var writes = new HashSet<string>(StringComparer.Ordinal);
            Collect(context.Schema, fragments, op.SelectionSet, root, "", writes, []);
            if (writes.Count <= 1)
                continue;

            context.ReportError(
                ErrorBuilder
                    .New()
                    .SetMessage(
                        "An operation may hold at most one machine event, start or discard; this one holds "
                            + $"{writes.Count} ({string.Join(", ", writes.Order(StringComparer.Ordinal))}). "
                            + "Send each in its own request."
                    )
                    .SetCode(Code)
                    .Build()
            );
            return;
        }
    }

    private static void Collect(
        ISchemaDefinition schema,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        SelectionSetNode selectionSet,
        ITypeDefinition? parent,
        string prefix,
        HashSet<string> into,
        HashSet<string> expanding
    )
    {
        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field:
                    if (
                        parent is not IComplexTypeDefinition complex
                        || !complex.Fields.TryGetField(field.Name.Value, out var definition)
                    )
                        break;
                    var path = prefix + (field.Alias?.Value ?? field.Name.Value);
                    if (MachineWriteField.IsMachineWrite(definition))
                        into.Add(path);
                    else if (
                        field.SelectionSet is not null
                        && NamespaceField.IsNamespace(definition)
                    )
                        Collect(
                            schema,
                            fragments,
                            field.SelectionSet,
                            definition.Type.NamedType(),
                            path + ".",
                            into,
                            expanding
                        );
                    break;

                case InlineFragmentNode inline:
                    Collect(
                        schema,
                        fragments,
                        inline.SelectionSet,
                        TypeCondition(schema, inline.TypeCondition) ?? parent,
                        prefix,
                        into,
                        expanding
                    );
                    break;

                case FragmentSpreadNode spread:
                    var name = spread.Name.Value;
                    if (!fragments.TryGetValue(name, out var fragment) || !expanding.Add(name))
                        break;
                    Collect(
                        schema,
                        fragments,
                        fragment.SelectionSet,
                        TypeCondition(schema, fragment.TypeCondition) ?? parent,
                        prefix,
                        into,
                        expanding
                    );
                    expanding.Remove(name);
                    break;
            }
        }
    }

    private static ITypeDefinition? TypeCondition(
        ISchemaDefinition schema,
        NamedTypeNode? condition
    ) =>
        condition is not null && schema.Types.TryGetType(condition.Name.Value, out var type)
            ? type
            : null;
}
