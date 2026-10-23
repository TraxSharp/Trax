using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Generator;

namespace Trax.Cli.Clients;

/// <summary>The language <c>trax client --operations</c> writes its client in.</summary>
internal enum OperationsLanguage
{
    /// <summary>The TypeScript client the React console sends through.</summary>
    TypeScript,

    /// <summary>A C# client over Trax.Client, for a .NET operator frontend.</summary>
    CSharp,
}

internal sealed partial class ClientGenerator
{
    /// <summary>The namespace the C# operations client is written in when none is named.</summary>
    public const string DefaultOperationsNamespace = "Trax.Operations";

    // The static classes every C# operations client declares, which no generated record may take. Everything the
    // client takes from Trax.Client or the BCL is written as global::, so a schema type of the same name (an input
    // named Optional or TraxClient) declares its own record and changes nothing else.
    private static readonly string[] CSharpRuntimeNames =
    [
        "TraxOperations",
        "TraxOperationsClient",
    ];

    /// <summary>
    /// Every file of a C# operations client, in path order: <c>Types.g.cs</c> with the schema's enums and inputs, and
    /// a file per document with its variables record, its result records, its descriptor (name, kind, id and the text
    /// the id is computed from, byte for byte) and the extension method on <c>TraxClient</c> that sends or follows
    /// it. Every type is internal: the client belongs to the app it is generated into. No
    /// <c>console-documents.json</c> is written; the host registers the documents from the TypeScript client's.
    /// </summary>
    public static IReadOnlyList<ClientFile> RenderCSharp(
        OperationsGenerateOptions options,
        string @namespace
    )
    {
        if (!IsNamespace(@namespace))
            throw new InvalidOperationException(
                $"--namespace '{@namespace}' is not a C# namespace (dotted identifiers, such as My.App.Operations)."
            );
        GraphQLDocument sdl;
        try
        {
            sdl = Parser.Parse(options.Schema);
        }
        catch (GraphQLParser.Exceptions.GraphQLParserException ex)
        {
            throw new InvalidOperationException(
                $"The operations schema is not GraphQL SDL ({ex.Message.Trim()}).",
                ex
            );
        }
        var types = new SdlTypes(sdl);
        var roots = OperationsRoots.Of(sdl);
        var documents = ClientDocumentSet.ReadOperations(options.Documents, types, roots);
        var fragments = documents.Fragments.ToDictionary(
            f => f.Key,
            f => f.Value.Definition,
            StringComparer.Ordinal
        );

        var names = new Names(rename: Rename);
        foreach (var name in CSharpRuntimeNames)
            names.Claim(name, "the client's runtime");
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in types.Declared)
            declared[type] = names.Claim(CSharp.TypeName(type), $"the GraphQL type {type}");
        var paths = new Names(StringComparer.OrdinalIgnoreCase, Rename);

        var writer = new CSharpWriter(types, declared, fragments, names);
        var files = new List<ClientFile> { new("Types.g.cs", writer.Types(@namespace)) };
        foreach (var operation in documents.Operations)
        {
            var owner = $"the console document {operation.Name} ({operation.Source})";
            var name = CSharp.TypeName(operation.Name);
            var path = paths.Claim($"Documents/{name}.g.cs", owner);
            files.Add(new(path, writer.Document(@namespace, operation, name, roots, owner)));
        }
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>Writes the C# operations client under <paramref name="outDir"/>.</summary>
    public static IReadOnlyList<ClientFile> WriteCSharp(
        OperationsGenerateOptions options,
        string @namespace,
        string outDir
    ) => Write(RenderCSharp(options, @namespace), outDir);

    /// <summary>What differs between the C# operations client the documents generate and the one under <paramref name="outDir"/>.</summary>
    public static IReadOnlyList<ClientDrift> CheckCSharp(
        OperationsGenerateOptions options,
        string @namespace,
        string outDir
    ) => Check(RenderCSharp(options, @namespace), outDir);

    private const string Rename = "Rename one of them.";

    private static bool IsNamespace(string text) =>
        text.Length > 0
        && text.Split('.')
            .All(part =>
                part.Length > 0
                && (char.IsLetter(part[0]) || part[0] == '_')
                && part.All(c => char.IsLetterOrDigit(c) || c == '_')
                && !CSharp.IsKeyword(part)
            );

    // Writes the C# text of the types and the documents.
    private sealed class CSharpWriter(
        SdlTypes types,
        IReadOnlyDictionary<string, string> declared,
        IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments,
        Names names
    )
    {
        private const string Indent = "    ";

        private const string Serialization = "global::System.Text.Json.Serialization.";

        private const string Client = "global::Trax.Client.";

        // The members of TraxOperations and of TraxOperationsClient, so no document's field or method takes the name
        // of its class or of another document's.
        private readonly Names _fields = Seeded("TraxOperations");

        private readonly Names _methods = Seeded("TraxOperationsClient");

        private static Names Seeded(string type)
        {
            var members = new Names(rename: Rename);
            members.Claim(type, $"the class {type} the client declares");
            return members;
        }

        public string Types(string @namespace)
        {
            var sb = Header(@namespace);
            foreach (var name in types.Declared)
            {
                sb.Append('\n');
                switch (types.Definition(name))
                {
                    case GraphQLEnumTypeDefinition @enum:
                        Enum(sb, @enum);
                        break;
                    case GraphQLInputObjectTypeDefinition input:
                        sb.Append(Summary(input.Description?.Value.ToString(), ""));
                        Record(
                            sb,
                            declared[name],
                            (input.Fields?.Items ?? []).Select(f =>
                                (
                                    f.Name.StringValue,
                                    f.Type,
                                    f.DefaultValue is not null,
                                    f.Description?.Value.ToString()
                                )
                            )
                        );
                        break;
                }
            }
            return sb.ToString();
        }

        private void Enum(StringBuilder sb, GraphQLEnumTypeDefinition @enum)
        {
            var name = declared[@enum.Name.StringValue];
            var members = new Names(rename: Rename);
            sb.Append(Summary(@enum.Description?.Value.ToString(), ""));
            sb.Append(
                $"[{Serialization}JsonConverter(typeof({Serialization}JsonStringEnumConverter<{name}>))]\n"
            );
            sb.Append($"internal enum {name}\n{{\n");
            foreach (var value in @enum.Values?.Items ?? [])
            {
                var wire = value.Name.StringValue;
                var member = members.Claim(
                    CSharp.EnumMember(wire),
                    $"the value {wire} of {@enum.Name.StringValue}"
                );
                sb.Append(Summary(value.Description?.Value.ToString(), Indent));
                sb.Append($"{Indent}[{Serialization}JsonStringEnumMemberName({Literal(wire)})]\n");
                sb.Append($"{Indent}{member},\n");
            }
            sb.Append("}\n");
        }

        // A record of values a client sends: a variables record or an input. A field that is nullable or has a default
        // may be left out, so it is an Optional; any other is required.
        private void Record(
            StringBuilder sb,
            string name,
            IEnumerable<(
                string Wire,
                GraphQLType Type,
                bool HasDefault,
                string? Description
            )> fields
        )
        {
            var members = new Names(rename: Rename);
            members.Claim(name, "the record itself");
            sb.Append($"internal sealed record {name}\n{{\n");
            var first = true;
            foreach (var (wire, type, hasDefault, description) in fields)
            {
                if (!first)
                    sb.Append('\n');
                first = false;
                var property = members.Claim(
                    CSharp.Member(wire, name),
                    $"the field {wire} of {name}"
                );
                var optional = type is not GraphQLNonNullType || hasDefault;
                var value = InputType(type);
                sb.Append(Summary(description, Indent));
                sb.Append($"{Indent}[{Serialization}JsonPropertyName({Literal(wire)})]\n");
                sb.Append(
                    optional
                        ? $"{Indent}public {Client}Optional<{Nullable(value, type)}> {property} {{ get; init; }}\n"
                        : $"{Indent}public required {value} {property} {{ get; init; }}\n"
                );
            }
            sb.Append("}\n");
        }

        public string Document(
            string @namespace,
            ClientOperation operation,
            string name,
            OperationsRoots roots,
            string owner
        )
        {
            var sb = Header(@namespace);
            var definition = operation.Operation;
            var (kind, root) = definition.Operation switch
            {
                OperationType.Mutation => ("Mutation", roots.Mutation),
                OperationType.Subscription => ("Subscription", roots.Subscription),
                _ => ("Query", roots.Query),
            };
            var variables = definition.Variables?.Items ?? [];
            var variablesType =
                variables.Count == 0
                    ? $"{Client}TraxNoVariables"
                    : names.Claim($"{name}Variables", $"the variables of {owner}");
            var resultType = names.Claim($"{name}Result", $"the result of {owner}");

            if (variables.Count > 0)
            {
                sb.Append('\n');
                sb.Append($"/// <summary>The variables {Doc(operation.Name)} takes.</summary>\n");
                Record(
                    sb,
                    variablesType,
                    variables.Select(v =>
                        (
                            v.Variable.Name.StringValue,
                            v.Type,
                            v.DefaultValue is not null,
                            (string?)null
                        )
                    )
                );
            }

            var records = new List<(string Name, string Summary, string Body)>();
            var rootType =
                types.Definition(root) as GraphQLObjectTypeDefinition
                ?? throw new InvalidOperationException(
                    $"The operations schema has no object type {root}, the root {owner} selects from."
                );
            ObjectRecord(
                rootType,
                definition.SelectionSet,
                resultType,
                $"What {Doc(operation.Name)} answers: the fields it selects.",
                owner,
                records
            );
            foreach (var (record, summary, body) in records)
            {
                sb.Append('\n');
                sb.Append($"/// <summary>{summary}</summary>\n");
                sb.Append($"internal sealed record {record}\n{{\n").Append(body).Append("}\n");
            }

            var subscription = definition.Operation == OperationType.Subscription;
            var required = variables.Any(v =>
                v.Type is GraphQLNonNullType && v.DefaultValue is null
            );
            var idText = operation.Id.Length == 0 ? "null" : Literal(operation.Id);
            _fields.Claim(name, owner);
            var method = _methods.Claim(subscription ? name : $"{name}Async", owner);
            sb.Append('\n');
            sb.Append("internal static partial class TraxOperations\n{\n");
            sb.Append(
                operation.Id.Length == 0
                    ? $"{Indent}/// <summary>The {kind.ToLowerInvariant()} {Doc(operation.Name)} ({Doc(operation.Source)}). A host runs it as text, behind its own gate, and never registers it.</summary>\n"
                    : $"{Indent}/// <summary>The {kind.ToLowerInvariant()} {Doc(operation.Name)} ({Doc(operation.Source)}), and the id a host that serves the console registers it under.</summary>\n"
            );
            sb.Append(
                $"{Indent}public static readonly {Client}TraxDocument<{variablesType}, {resultType}> {name} = new(\n"
                    + $"{Indent}{Indent}{Literal(operation.Name)},\n"
                    + $"{Indent}{Indent}{Client}TraxOperationKind.{kind},\n"
                    + $"{Indent}{Indent}{idText},\n"
                    + $"{Indent}{Indent}{Literal(operation.Text)}\n"
                    + $"{Indent});\n"
            );
            sb.Append("}\n\n");

            sb.Append("internal static partial class TraxOperationsClient\n{\n");
            var parameter =
                variables.Count == 0 ? ""
                : required ? $"{variablesType} variables, "
                : $"{variablesType}? variables, ";
            var value =
                variables.Count == 0 ? $"{Client}TraxNoVariables.Instance"
                : required ? "variables"
                : "variables ?? new()";
            var document = $"global::{@namespace}.TraxOperations.{name}";
            if (subscription)
            {
                sb.Append(
                    $"{Indent}/// <summary>Follows {Doc(operation.Name)} until the returned handle is disposed.</summary>\n"
                );
                sb.Append(
                    $"{Indent}public static global::System.IDisposable {method}(\n"
                        + $"{Indent}{Indent}this {Client}TraxClient client,\n"
                        + (
                            parameter.Length == 0
                                ? ""
                                : $"{Indent}{Indent}{parameter.TrimEnd(' ', ',')},\n"
                        )
                        + $"{Indent}{Indent}global::System.Action<{Client}TraxStreamEvent<{resultType}>> onEvent\n"
                        + $"{Indent}) => client.Subscribe({document}, {value}, onEvent);\n"
                );
            }
            else
            {
                sb.Append(
                    $"{Indent}/// <summary>Sends {Doc(operation.Name)}; a refusal or a transport failure is in the answer.</summary>\n"
                );
                sb.Append(
                    $"{Indent}public static global::System.Threading.Tasks.Task<{Client}TraxAnswer<{resultType}>> {method}(\n"
                        + $"{Indent}{Indent}this {Client}TraxClient client,\n"
                        + (
                            parameter.Length == 0
                                ? ""
                                : $"{Indent}{Indent}{parameter.TrimEnd(' ', ',')},\n"
                        )
                        + $"{Indent}{Indent}global::System.Threading.CancellationToken cancellationToken = default\n"
                        + $"{Indent}) => client.SendAsync({document}, {value}, cancellationToken);\n"
                );
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        // The record a selection on one object type reads into, and every record below it, parents first. A nested
        // record is named after its parent and the key it sits under, so each name is the path to it.
        private void ObjectRecord(
            GraphQLObjectTypeDefinition objectType,
            GraphQLSelectionSet? selection,
            string name,
            string summary,
            string owner,
            List<(string Name, string Summary, string Body)> records
        )
        {
            var typeName = objectType.Name.StringValue;
            var fields = new List<(string Key, GraphQLField Field, List<ASTNode> Below)>();
            SelectionTypes.Collect(types, fragments, selection, typeName, fields);
            var at = records.Count;
            records.Add((name, summary, ""));
            var members = new Names(rename: Rename);
            members.Claim(name, "the record itself");
            var body = new StringBuilder();
            var below = new List<Action>();
            foreach (var (key, field, nested) in fields)
            {
                if (body.Length > 0)
                    body.Append('\n');
                var property = members.Claim(
                    CSharp.Member(key, name),
                    $"the key {key} {owner} selects on {typeName}"
                );
                if (field.Name.StringValue == "__typename")
                {
                    body.Append($"{Indent}/// <summary>The value's type, {typeName}.</summary>\n");
                    body.Append($"{Indent}[{Serialization}JsonPropertyName({Literal(key)})]\n");
                    body.Append($"{Indent}public required string {property} {{ get; init; }}\n");
                    continue;
                }
                var definition =
                    objectType.Fields?.Items.FirstOrDefault(f =>
                        f.Name.StringValue == field.Name.StringValue
                    )
                    ?? throw new InvalidOperationException(
                        $"{owner} selects {typeName}.{field.Name.StringValue}, which the operations schema does not hold."
                    );
                var childName = $"{name}{CSharp.TypeName(key)}";
                var child = new GraphQLSelectionSet(nested);
                var type = OutputType(
                    definition.Type,
                    childName,
                    () =>
                        below.Add(() =>
                            ObjectRecord(
                                (GraphQLObjectTypeDefinition)
                                    types.Definition(Named(definition.Type))!,
                                child,
                                names.Claim(childName, $"the key {key} of {owner}"),
                                $"{typeName}.{Doc(field.Name.StringValue)} as {Doc(owner)} selects it.",
                                owner,
                                records
                            )
                        ),
                    owner
                );
                body.Append(Summary(definition.Description?.Value.ToString(), Indent));
                body.Append($"{Indent}[{Serialization}JsonPropertyName({Literal(key)})]\n");
                body.Append(
                    definition.Type is GraphQLNonNullType
                        ? $"{Indent}public required {type} {property} {{ get; init; }}\n"
                        : $"{Indent}public {type} {property} {{ get; init; }}\n"
                );
            }
            records[at] = (name, summary, body.ToString());
            foreach (var write in below)
                write();
        }

        // The C# type a value of an output field arrives as: nullable where the schema says so, an object as the
        // record named childName, which onObject writes.
        private string OutputType(GraphQLType type, string childName, Action onObject, string owner)
        {
            string Bare(GraphQLType t) =>
                t switch
                {
                    GraphQLNonNullType nonNull => Bare(nonNull.Type),
                    GraphQLListType list => $"{ReadOnlyList}<{Of(list.Type)}>",
                    GraphQLNamedType named => Leaf(named.Name.StringValue),
                    _ => CSharp.JsonElement,
                };
            string Of(GraphQLType t) => t is GraphQLNonNullType ? Bare(t) : Nullable(Bare(t), t);
            string Leaf(string name)
            {
                switch (types.Definition(name))
                {
                    case GraphQLObjectTypeDefinition:
                        onObject();
                        return childName;
                    case GraphQLInterfaceTypeDefinition or GraphQLUnionTypeDefinition:
                        throw new InvalidOperationException(
                            $"{owner} selects the {(types.Definition(name) is GraphQLUnionTypeDefinition ? "union" : "interface")} {name}, which the C# client does not type yet. Select through a field of an object type."
                        );
                    default:
                        return InputLeaf(name);
                }
            }
            return Of(type);
        }

        // The C# type a value sent as type is written as: nullable where the schema says so.
        private string InputType(GraphQLType type)
        {
            string Bare(GraphQLType t) =>
                t switch
                {
                    GraphQLNonNullType nonNull => Bare(nonNull.Type),
                    GraphQLListType list => $"{ReadOnlyList}<{InputType(list.Type)}>",
                    GraphQLNamedType named => InputLeaf(named.Name.StringValue),
                    _ => CSharp.JsonElement,
                };
            return type is GraphQLNonNullType ? Bare(type) : Nullable(Bare(type), type);
        }

        private string InputLeaf(string name) =>
            declared.TryGetValue(name, out var declaredName) ? declaredName
            : CSharp.Scalars.TryGetValue(name, out var scalar) ? scalar
            : CSharp.JsonElement;

        private const string ReadOnlyList = "global::System.Collections.Generic.IReadOnlyList";

        private static string Nullable(string type, GraphQLType graphQLType) =>
            graphQLType is GraphQLNonNullType || type.EndsWith('?') ? type : type + "?";

        private static string Named(GraphQLType type) =>
            type switch
            {
                GraphQLNonNullType nonNull => Named(nonNull.Type),
                GraphQLListType list => Named(list.Type),
                GraphQLNamedType named => named.Name.StringValue,
                _ => "",
            };

        private static StringBuilder Header(string @namespace) =>
            new StringBuilder(OperationsHeader)
                .Append("// <auto-generated/>\n")
                .Append("#nullable enable\n\n")
                .Append($"namespace {@namespace};\n");

        private static string Summary(string? description, string indent) =>
            string.IsNullOrWhiteSpace(description)
                ? ""
                : $"{indent}/// <summary>{GeneratedText.DocComment(description)}</summary>\n";

        private static string Doc(string text) => GeneratedText.DocComment(text);

        // A C# regular string literal holding text exactly: JSON's escapes are all C# escapes, and every character
        // outside printable ASCII is escaped, so no line terminator C# knows can end the literal.
        private static string Literal(string text) => TypeScript.Literal(text);
    }

    // C# names for GraphQL ones.
    private static class CSharp
    {
        public const string JsonElement = "global::System.Text.Json.JsonElement";

        public static readonly IReadOnlyDictionary<string, string> Scalars = new Dictionary<
            string,
            string
        >(StringComparer.Ordinal)
        {
            ["String"] = "string",
            ["ID"] = "string",
            ["UUID"] = "global::System.Guid",
            ["DateTime"] = "global::System.DateTimeOffset",
            // Formats a host writes that no BCL type reads as they are: kept as the text that arrives.
            ["Date"] = "string",
            ["LocalDate"] = "string",
            ["LocalTime"] = "string",
            ["LocalDateTime"] = "string",
            ["TimeSpan"] = "string",
            ["Duration"] = "string",
            ["URL"] = "string",
            ["Url"] = "string",
            ["Uri"] = "string",
            ["ByteArray"] = "string",
            ["Int"] = "int",
            ["Short"] = "short",
            ["Byte"] = "byte",
            ["Long"] = "long",
            ["Float"] = "double",
            ["Decimal"] = "decimal",
            ["Boolean"] = "bool",
            ["Any"] = JsonElement,
            ["JSON"] = JsonElement,
        };

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "abstract",
            "as",
            "base",
            "bool",
            "break",
            "byte",
            "case",
            "catch",
            "char",
            "checked",
            "class",
            "const",
            "continue",
            "decimal",
            "default",
            "delegate",
            "do",
            "double",
            "else",
            "enum",
            "event",
            "explicit",
            "extern",
            "false",
            "finally",
            "fixed",
            "float",
            "for",
            "foreach",
            "goto",
            "if",
            "implicit",
            "in",
            "int",
            "interface",
            "internal",
            "is",
            "lock",
            "long",
            "namespace",
            "new",
            "null",
            "object",
            "operator",
            "out",
            "override",
            "params",
            "private",
            "protected",
            "public",
            "readonly",
            "ref",
            "return",
            "sbyte",
            "sealed",
            "short",
            "sizeof",
            "stackalloc",
            "static",
            "string",
            "struct",
            "switch",
            "this",
            "throw",
            "true",
            "try",
            "typeof",
            "uint",
            "ulong",
            "unchecked",
            "unsafe",
            "ushort",
            "using",
            "virtual",
            "void",
            "volatile",
            "while",
        };

        public static bool IsKeyword(string name) => Keywords.Contains(name);

        // A type name: the GraphQL name with its first letter raised and anything that is not a letter, digit or
        // underscore dropped.
        public static string TypeName(string name)
        {
            var clean = new string(
                name.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray()
            ).TrimStart('_');
            if (clean.Length == 0)
                return "Value";
            if (char.IsAsciiDigit(clean[0]))
                clean = "_" + clean;
            return char.ToUpperInvariant(clean[0]) + clean[1..];
        }

        // A property: the type name of the key, with Value after it when it would take its record's own name.
        public static string Member(string key, string record)
        {
            var name = TypeName(key);
            return name == record ? name + "Value" : name;
        }

        // An enum member: SCREAMING_CASE as PascalCase (LAST24_HOURS is Last24Hours), anything else as a type name.
        public static string EnumMember(string wire)
        {
            if (wire.Any(char.IsAsciiLetterLower))
                return TypeName(wire);
            var parts = wire.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant());
            return TypeName(string.Concat(parts));
        }
    }
}
