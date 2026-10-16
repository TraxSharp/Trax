using System.Text;
using GraphQLParser.AST;

namespace Trax.Cli.Clients;

/// <summary>
/// The GraphQL types a client manifest lists. What a client sends is written once, as TypeScript: an enum as a union
/// of its values, an input as an interface whose nullable fields are optional. What it is answered with is not: an
/// object, interface or union is typed per document, from what the document selects (<see cref="SelectionTypes"/>),
/// so this class only looks those up. A scalar is the TypeScript type its value arrives as; one this generator does
/// not know is <c>unknown</c>, never a guess.
/// </summary>
internal sealed class SdlTypes
{
    private static readonly Dictionary<string, string> Scalars = new(StringComparer.Ordinal)
    {
        ["String"] = "string",
        ["ID"] = "string",
        ["UUID"] = "string",
        ["DateTime"] = "string",
        ["Date"] = "string",
        ["LocalDate"] = "string",
        ["LocalTime"] = "string",
        ["LocalDateTime"] = "string",
        ["TimeSpan"] = "string",
        ["URL"] = "string",
        ["Url"] = "string",
        ["Uri"] = "string",
        ["ByteArray"] = "string",
        ["Int"] = "number",
        ["Float"] = "number",
        ["Long"] = "number",
        ["Short"] = "number",
        ["Byte"] = "number",
        ["Decimal"] = "number",
        ["Boolean"] = "boolean",
        ["Any"] = "unknown",
        ["JSON"] = "unknown",
    };

    private readonly Dictionary<string, ASTNode> _definitions = new(StringComparer.Ordinal);

    public SdlTypes(GraphQLDocument sdl)
    {
        foreach (var definition in sdl.Definitions)
            if (definition is GraphQLTypeDefinition type and not GraphQLScalarTypeDefinition)
                _definitions[type.Name.StringValue] = definition;
    }

    /// <summary>The names <see cref="Render"/> exports, in name order: every enum and input.</summary>
    public IEnumerable<string> Declared =>
        _definitions
            .Where(d => IsDeclared(d.Value))
            .Select(d => d.Key)
            .Order(StringComparer.Ordinal);

    private static bool IsDeclared(ASTNode definition) =>
        definition is GraphQLEnumTypeDefinition or GraphQLInputObjectTypeDefinition;

    /// <summary>The definition of the type <paramref name="name"/>, or null when the manifest holds none.</summary>
    public ASTNode? Definition(string name) => _definitions.GetValueOrDefault(name);

    /// <summary>
    /// The object types a value of the union or interface <paramref name="name"/> can be, in name order: a union's
    /// members, or every object type the manifest holds that implements the interface.
    /// </summary>
    public IEnumerable<string> PossibleTypes(string name) =>
        (
            Definition(name) switch
            {
                GraphQLUnionTypeDefinition union => (union.Types?.Items ?? [])
                    .Select(t => t.Name.StringValue)
                    .ToList(),
                GraphQLInterfaceTypeDefinition => _definitions
                    .Values.OfType<GraphQLObjectTypeDefinition>()
                    .Where(o => Implements(o, name))
                    .Select(o => o.Name.StringValue)
                    .ToList(),
                _ => [],
            }
        ).Order(StringComparer.Ordinal);

    /// <summary>
    /// Whether a fragment on <paramref name="condition"/> applies to a value of the object type
    /// <paramref name="objectType"/>: the same type, an interface it implements, or a union it is a member of.
    /// </summary>
    public bool Applies(string condition, string objectType) =>
        condition == objectType
        || Definition(condition) switch
        {
            GraphQLInterfaceTypeDefinition => Definition(objectType)
                is GraphQLObjectTypeDefinition o
                && Implements(o, condition),
            GraphQLUnionTypeDefinition union => (union.Types?.Items ?? []).Any(t =>
                t.Name.StringValue == objectType
            ),
            _ => false,
        };

    private static bool Implements(GraphQLObjectTypeDefinition objectType, string @interface) =>
        (objectType.Interfaces?.Items ?? []).Any(i => i.Name.StringValue == @interface);

    /// <summary>A scalar or enum named <paramref name="name"/>, as a value of it arrives.</summary>
    public string Leaf(string name) => Scalar(name);

    /// <summary>
    /// A type written as SDL writes it (<c>[Order!]!</c>), or null when there is none. Text that is not one type is
    /// refused naming <paramref name="owner"/>, never in the parser's words.
    /// </summary>
    public static GraphQLType? ParseType(string? text, string owner)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        GraphQLDocument parsed;
        try
        {
            parsed = GraphQLParser.Parser.Parse($"type T {{ f: {text} }}");
        }
        catch (GraphQLParser.Exceptions.GraphQLParserException ex)
        {
            throw NotAType(text, owner, ex);
        }
        // Text that closes the type it is parsed in (`Int } type X { y: Int`) parses, as two definitions.
        return parsed.Definitions is [GraphQLObjectTypeDefinition { Fields.Items: [var field] }]
            ? field.Type
            : throw NotAType(text, owner, null);
    }

    private static InvalidOperationException NotAType(
        string text,
        string owner,
        Exception? inner
    ) =>
        new(
            $"The manifest says {owner} answers with {TypeScript.Literal(text)}, which is not a GraphQL type. "
                + "Export the manifest again from the host.",
            inner
        );

    /// <summary>The named type <paramref name="type"/> holds when it is one this file declares, else null.</summary>
    public string? NameOf(GraphQLType type)
    {
        var name = Named(type);
        return _definitions.ContainsKey(name) ? name : null;
    }

    /// <summary>The declared types <paramref name="types"/> name.</summary>
    public IEnumerable<string> NamesIn(IEnumerable<GraphQLType> types) =>
        types.Select(NameOf).Where(n => n is not null).Select(n => n!);

    /// <summary>A value sent as <paramref name="type"/>: null allowed where the type is nullable.</summary>
    public string InputType(GraphQLType type) =>
        type is GraphQLNonNullType nonNull ? Bare(nonNull.Type) : $"{Bare(type)} | null";

    /// <summary>
    /// The file every train imports the types it sends from: each enum and input in name order, each name claimed once
    /// among everything the client exports.
    /// </summary>
    public string Render()
    {
        var sb = new StringBuilder(
            "// Generated by trax client from the host's client manifest. Do not edit.\n"
        );
        foreach (
            var (name, definition) in _definitions
                .Where(d => IsDeclared(d.Value))
                .OrderBy(d => d.Key, StringComparer.Ordinal)
        )
        {
            var identifier = TypeScript.Identifier(name, "GraphQL type");
            sb.Append('\n');
            switch (definition)
            {
                case GraphQLEnumTypeDefinition @enum:
                    sb.Append(TypeScript.Doc(@enum.Description?.Value.ToString()));
                    sb.Append($"export type {identifier} = ")
                        .Append(
                            string.Join(
                                " | ",
                                (@enum.Values?.Items ?? []).Select(v =>
                                    TypeScript.Literal(v.Name.StringValue)
                                )
                            )
                        )
                        .Append(";\n");
                    break;
                case GraphQLInputObjectTypeDefinition input:
                    sb.Append(TypeScript.Doc(input.Description?.Value.ToString()));
                    sb.Append($"export interface {identifier} {{\n");
                    foreach (var field in input.Fields?.Items ?? [])
                    {
                        sb.Append(TypeScript.Doc(field.Description?.Value.ToString(), "  "));
                        var optional =
                            field.Type is not GraphQLNonNullType || field.DefaultValue is not null;
                        sb.Append(
                            $"  {Property(field.Name.StringValue)}{(optional ? "?" : "")}: {InputType(field.Type)};\n"
                        );
                    }
                    sb.Append("}\n");
                    break;
            }
        }
        return sb.ToString();
    }

    // A property name: an identifier as it is, anything else quoted.
    internal static string Property(string name) =>
        TypeScript.IsIdentifier(name) ? name : TypeScript.Literal(name);

    private string Bare(GraphQLType type) =>
        type switch
        {
            GraphQLNonNullType nonNull => Bare(nonNull.Type),
            GraphQLListType list => $"Array<{InputType(list.Type)}>",
            GraphQLNamedType named => Scalar(named.Name.StringValue),
            _ => "unknown",
        };

    private string Scalar(string name) =>
        Scalars.TryGetValue(name, out var ts) ? ts
        : _definitions.ContainsKey(name) ? name
        : "unknown";

    private static string Named(GraphQLType type) =>
        type switch
        {
            GraphQLNonNullType nonNull => Named(nonNull.Type),
            GraphQLListType list => Named(list.Type),
            GraphQLNamedType named => named.Name.StringValue,
            _ => "",
        };
}
