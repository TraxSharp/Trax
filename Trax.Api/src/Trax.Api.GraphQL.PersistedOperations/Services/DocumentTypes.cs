using System.Text.RegularExpressions;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using Trax.Api.GraphQL.Validation;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The GraphQL types a set of documents reaches, and their SDL: what a client manifest lists and what a train
/// document's shape hash covers.
/// </summary>
internal static partial class DocumentTypes
{
    /// <summary>Every named definition of the schema's SDL, by name.</summary>
    public static IReadOnlyDictionary<string, INamedSyntaxNode> Definitions(
        ISchemaDefinition schema
    ) =>
        Utf8GraphQLParser
            .Parse(schema.ToString())
            .Definitions.OfType<INamedSyntaxNode>()
            .GroupBy(d => d.Name.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>
    /// The named types the documents reach, in name order: their variables' types with every input type those hold,
    /// and the types of the fields they select, through every fragment.
    /// </summary>
    public static SortedSet<string> Reached(ISchemaDefinition schema, IEnumerable<string> documents)
    {
        var reached = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in documents)
        {
            var document = Utf8GraphQLParser.Parse(text);
            foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
            {
                foreach (var variable in operation.VariableDefinitions)
                    Reach(schema, variable.Type.NamedType().Name.Value, reached);
                var root = operation.Operation switch
                {
                    OperationType.Mutation => schema.MutationType,
                    OperationType.Subscription => schema.SubscriptionType,
                    _ => schema.QueryType,
                };
                if (root is not null)
                    Select(schema, root, operation.SelectionSet, reached);
            }
        }
        return reached;
    }

    /// <summary>
    /// The SDL of every type the documents reach but the <see cref="Wrappers"/>, in name order, so the fields a client
    /// cannot send, the operations namespace and every sibling train and query model among them, are never listed.
    /// </summary>
    public static string Sdl(
        ISchemaDefinition schema,
        IEnumerable<string> documents,
        IEnumerable<string>? leftOut = null
    )
    {
        var definitions = Definitions(schema);
        var wrappers = Wrappers(schema);
        var reached = Reached(schema, documents);
        foreach (var field in leftOut ?? [])
            ReachField(schema, field, wrappers, reached);
        return string.Join(
                "\n\n",
                reached
                    .Where(name => !wrappers.Contains(name) && definitions.ContainsKey(name))
                    .Select(name => definitions[name].ToString(indented: true))
            )
            .Replace("\r\n", "\n");
    }

    /// <summary>
    /// The types that hold operations rather than data: the root types, and every type a namespace field reaches from
    /// them, which are <c>dispatch</c>, <c>discover</c>, <c>operations</c>, <c>machines</c> and the namespaces under
    /// them. Each holds every sibling operation, so a hash over it would move whenever another operation is exposed,
    /// and a client manifest that listed it would list them all. Found by the marker the schema puts on a namespace
    /// field, never by a type's name, so a consumer's type named like one is still data.
    /// </summary>
    public static HashSet<string> Wrappers(ISchemaDefinition schema)
    {
        var wrappers = Roots(schema);
        var pending = new Queue<string>(wrappers);
        while (pending.TryDequeue(out var name))
        {
            if (
                !schema.Types.TryGetType(name, out var type)
                || type is not IComplexTypeDefinition complex
            )
                continue;
            foreach (var field in complex.Fields)
                if (NamespaceField.IsNamespace(field) && wrappers.Add(field.Type.NamedType().Name))
                    pending.Enqueue(field.Type.NamedType().Name);
        }
        return wrappers;
    }

    /// <summary>A definition's SDL without its descriptions, so editing a doc comment changes nothing.</summary>
    public static string WithoutDescriptions(INamedSyntaxNode node)
    {
        var text = node.ToString(indented: true).Replace("\r\n", "\n");
        text = BlockDescription().Replace(text, "");
        return LineDescription().Replace(text, "");
    }

    private static HashSet<string> Roots(ISchemaDefinition schema) =>
        new[] { schema.QueryType, schema.MutationType, schema.SubscriptionType }
            .Where(t => t is not null)
            .Select(t => t!.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static void Select(
        ISchemaDefinition schema,
        ITypeDefinition type,
        SelectionSetNode? selections,
        SortedSet<string> reached
    )
    {
        if (selections is null)
            return;
        foreach (var selection in selections.Selections)
        {
            switch (selection)
            {
                case FieldNode field
                    when type is IComplexTypeDefinition complex
                        && complex.Fields.TryGetField(field.Name.Value, out var definition):
                    foreach (var argument in definition.Arguments)
                        Reach(schema, argument.Type.NamedType().Name, reached);
                    var named = definition.Type.NamedType();
                    Reach(schema, named.Name, reached);
                    Select(schema, named, field.SelectionSet, reached);
                    break;
                case InlineFragmentNode fragment:
                    var on =
                        fragment.TypeCondition is { } condition
                        && schema.Types.TryGetType(condition.Name.Value, out var conditionType)
                            ? conditionType
                            : type;
                    Reach(schema, on.Name, reached);
                    Select(schema, on, fragment.SelectionSet, reached);
                    break;
            }
        }
    }

    // The type of a field a generated document left out (`Customer.orders`, the last segment of its path) and every
    // type it reaches, so a client may write a document that selects it.
    private static void ReachField(
        ISchemaDefinition schema,
        string path,
        HashSet<string> wrappers,
        SortedSet<string> reached
    )
    {
        var segment = path.Split(" > ")[^1];
        var dot = segment.IndexOf('.');
        if (
            dot < 0
            || !schema.Types.TryGetType(segment[..dot], out var owner)
            || owner is not IComplexTypeDefinition complex
            || !complex.Fields.TryGetField(segment[(dot + 1)..], out var field)
        )
            return;
        var pending = new Queue<ITypeDefinition>();
        foreach (var argument in field.Arguments)
            Reach(schema, argument.Type.NamedType().Name, reached);
        pending.Enqueue(field.Type.NamedType());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var type))
        {
            if (wrappers.Contains(type.Name) || !seen.Add(type.Name))
                continue;
            Reach(schema, type.Name, reached);
            IEnumerable<ITypeDefinition> next = type switch
            {
                IComplexTypeDefinition c => c
                    .Fields.Where(f => !f.Name.StartsWith("__", StringComparison.Ordinal))
                    .SelectMany(f =>
                        f.Arguments.Select(a => a.Type.NamedType()).Append(f.Type.NamedType())
                    ),
                IUnionTypeDefinition u => u.Types,
                _ => [],
            };
            if (type is IInterfaceTypeDefinition @interface)
                next = next.Concat(
                    schema
                        .Types.OfType<IObjectTypeDefinition>()
                        .Where(o => o.Implements.Any(i => i.Name == @interface.Name))
                );
            foreach (var t in next)
                pending.Enqueue(t);
        }
    }

    // A named type, and every input type an input object holds.
    private static void Reach(ISchemaDefinition schema, string name, SortedSet<string> reached)
    {
        if (!reached.Add(name) || !schema.Types.TryGetType(name, out var type))
            return;
        if (type is IInputObjectTypeDefinition input)
            foreach (var field in input.Fields)
                Reach(schema, field.Type.NamedType().Name, reached);
    }

    // A block string on lines of its own, and a string alone on its line: where the printer puts a description. A
    // default value is never alone on its line.
    [GeneratedRegex(@"^[ \t]*""""""[\s\S]*?""""""[ \t]*\n?", RegexOptions.Multiline)]
    private static partial Regex BlockDescription();

    [GeneratedRegex(@"^[ \t]*""(?:[^""\\\n]|\\.)*""[ \t]*\n?", RegexOptions.Multiline)]
    private static partial Regex LineDescription();
}
