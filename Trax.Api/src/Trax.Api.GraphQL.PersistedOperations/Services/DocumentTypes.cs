using System.Text.RegularExpressions;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;

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
    /// The SDL of every type the documents reach but the root operation types, in name order, so the fields a client
    /// cannot send, the operations namespace among them, are never listed.
    /// </summary>
    public static string Sdl(ISchemaDefinition schema, IEnumerable<string> documents)
    {
        var definitions = Definitions(schema);
        var roots = Roots(schema);
        return string.Join(
                "\n\n",
                Reached(schema, documents)
                    .Where(name => !roots.Contains(name) && definitions.ContainsKey(name))
                    .Select(name => definitions[name].ToString(indented: true))
            )
            .Replace("\r\n", "\n");
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a type that holds operations rather than data: a root type, or a type the
    /// trains' <c>dispatch</c> and <c>discover</c> fields, their namespaces, or the <c>machines</c> root reach through.
    /// Each holds every sibling operation, so a hash over it would move whenever another operation is exposed.
    /// </summary>
    public static bool IsWrapper(ISchemaDefinition schema, string name) =>
        Roots(schema).Contains(name)
        || name.EndsWith("DispatchMutations", StringComparison.Ordinal)
        || name.EndsWith("DiscoverQueries", StringComparison.Ordinal)
        || name is "MachinesMutations" or "MachinesQueries";

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
