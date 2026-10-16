using System.Text;
using GraphQLParser.AST;

namespace Trax.Cli.Clients;

/// <summary>
/// The TypeScript type of what a document selects: an object as the fields its selection names, each typed from the
/// manifest's SDL, an alias naming its key; a union or interface as one object per possible type, told apart by
/// <c>__typename</c>; a scalar or enum as <see cref="SdlTypes"/> writes it. A field the document does not select is
/// not in the type, so a type never promises what the answer lacks.
/// </summary>
internal sealed class SelectionTypes(
    SdlTypes sdl,
    IReadOnlyDictionary<string, GraphQLFragmentDefinition>? fragments = null
)
{
    /// <summary>
    /// The type of a value of <paramref name="type"/> answered with <paramref name="selection"/>, written to start at
    /// <paramref name="indent"/>. Every enum or input it names is added to <paramref name="used"/>.
    /// </summary>
    public string Of(
        GraphQLType type,
        GraphQLSelectionSet? selection,
        string owner,
        ISet<string> used,
        int indent = 0
    ) =>
        type is GraphQLNonNullType nonNull
            ? Bare(nonNull.Type, selection, owner, used, indent)
            : $"{Bare(type, selection, owner, used, indent)} | null";

    /// <summary>
    /// The selection of the field <paramref name="path"/> names, from the operation's root down: a train's field under
    /// <c>dispatch</c> or <c>discover</c> and its namespace.
    /// </summary>
    public static GraphQLSelectionSet? SelectionAt(
        GraphQLOperationDefinition operation,
        IReadOnlyList<string> path,
        string owner
    )
    {
        var selection = operation.SelectionSet;
        foreach (var name in path)
        {
            var field =
                selection
                    ?.Selections.OfType<GraphQLField>()
                    .FirstOrDefault(f => f.Name.StringValue == name)
                ?? throw new InvalidOperationException(
                    $"The document of {owner} does not select '{string.Join(".", path)}'. Export the manifest again "
                        + "from the host."
                );
            selection = field.SelectionSet;
        }
        return selection;
    }

    private string Bare(
        GraphQLType type,
        GraphQLSelectionSet? selection,
        string owner,
        ISet<string> used,
        int indent
    ) =>
        type switch
        {
            GraphQLNonNullType nonNull => Bare(nonNull.Type, selection, owner, used, indent),
            GraphQLListType list => $"Array<{Of(list.Type, selection, owner, used, indent)}>",
            GraphQLNamedType named => Named(named.Name.StringValue, selection, owner, used, indent),
            _ => "unknown",
        };

    private string Named(
        string name,
        GraphQLSelectionSet? selection,
        string owner,
        ISet<string> used,
        int indent
    )
    {
        switch (sdl.Definition(name))
        {
            case GraphQLObjectTypeDefinition objectType:
                return Object(objectType, selection, owner, used, indent);
            case GraphQLInterfaceTypeDefinition or GraphQLUnionTypeDefinition:
                var members = sdl.PossibleTypes(name)
                    .Select(possible =>
                        sdl.Definition(possible) is GraphQLObjectTypeDefinition objectType
                            ? Object(objectType, selection, owner, used, indent)
                            : $"{{ __typename: {TypeScript.Literal(possible)} }}"
                    )
                    .ToList();
                return members.Count == 0 ? "never" : string.Join(" | ", members);
            case GraphQLEnumTypeDefinition:
                used.Add(name);
                return sdl.Leaf(name);
            default:
                return sdl.Leaf(name);
        }
    }

    // The fields a selection names on one concrete type, in the order it names them: its own, those of an inline
    // fragment on that type or on a type it belongs to, and those under one key merged into one.
    private string Object(
        GraphQLObjectTypeDefinition objectType,
        GraphQLSelectionSet? selection,
        string owner,
        ISet<string> used,
        int indent
    )
    {
        var typeName = objectType.Name.StringValue;
        var fields = new List<(string Key, GraphQLField Field, List<ASTNode> Below)>();
        Collect(selection, typeName, fields);
        if (fields.Count == 0)
            return "Record<string, never>";

        var pad = new string(' ', indent + 2);
        var sb = new StringBuilder("{\n");
        foreach (var (key, field, below) in fields)
        {
            var name = field.Name.StringValue;
            if (name == "__typename")
            {
                sb.Append(pad)
                    .Append(SdlTypes.Property(key))
                    .Append(": ")
                    .Append(TypeScript.Literal(typeName))
                    .Append(";\n");
                continue;
            }
            var definition =
                objectType.Fields?.Items.FirstOrDefault(f => f.Name.StringValue == name)
                ?? throw new InvalidOperationException(
                    $"The document of {owner} selects {typeName}.{name}, which the manifest's types do not hold. "
                        + "Export the manifest again from the host."
                );
            sb.Append(TypeScript.Doc(definition.Description?.Value.ToString(), pad));
            var nested = below.Count == 0 ? null : new GraphQLSelectionSet(below);
            sb.Append(pad)
                .Append(SdlTypes.Property(key))
                .Append(": ")
                .Append(Of(definition.Type, nested, owner, used, indent + 2))
                .Append(";\n");
        }
        return sb.Append(new string(' ', indent)).Append('}').ToString();
    }

    private void Collect(
        GraphQLSelectionSet? selection,
        string typeName,
        List<(string Key, GraphQLField Field, List<ASTNode> Below)> fields
    )
    {
        foreach (var item in selection?.Selections ?? [])
            switch (item)
            {
                case GraphQLField field:
                    var key = field.Alias?.Name.StringValue ?? field.Name.StringValue;
                    var at = fields.FindIndex(f => f.Key == key);
                    if (at < 0)
                    {
                        fields.Add((key, field, []));
                        at = fields.Count - 1;
                    }
                    fields[at].Below.AddRange(field.SelectionSet?.Selections ?? []);
                    break;
                case GraphQLInlineFragment fragment
                    when fragment.TypeCondition is null
                        || sdl.Applies(fragment.TypeCondition.Type.Name.StringValue, typeName):
                    Collect(fragment.SelectionSet, typeName, fields);
                    break;
                // A named fragment's fields are written out where it is spread: the type is the same shape as the
                // fragment's own, so a value typed by either fits the other.
                case GraphQLFragmentSpread spread
                    when fragments?.GetValueOrDefault(spread.FragmentName.Name.StringValue)
                        is { } definition
                        && sdl.Applies(definition.TypeCondition.Type.Name.StringValue, typeName):
                    Collect(definition.SelectionSet, typeName, fields);
                    break;
            }
    }
}
