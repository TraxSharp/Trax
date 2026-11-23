using System.Globalization;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Exceptions;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Core.Extensions;

namespace Trax.Cli.Schema.GraphQL;

public class GraphQLSchemaParser : ISchemaParser
{
    private static readonly Dictionary<string, string> ScalarMap = new(StringComparer.Ordinal)
    {
        ["String"] = "string",
        ["Int"] = "int",
        ["Float"] = "double",
        ["Boolean"] = "bool",
        ["ID"] = "string",
        ["DateTime"] = "DateTime",
        ["Date"] = "DateOnly",
        ["Long"] = "long",
        ["BigInt"] = "long",
        ["Decimal"] = "decimal",
    };

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "__Schema",
        "__Type",
        "__Field",
        "__InputValue",
        "__EnumValue",
        "__Directive",
        "__DirectiveLocation",
    };

    // Custom scalars, and interfaces and unions, by schema name. None becomes a model: a custom scalar is
    // written as string and an interface or union as object, each with a TODO on the property.
    private readonly HashSet<string> _customScalars = new(StringComparer.Ordinal);
    private readonly HashSet<string> _abstractTypes = new(StringComparer.Ordinal);

    private readonly TextWriter _warnings;

    /// <summary>A parser that drops the warnings about what a schema has that trax skips.</summary>
    public GraphQLSchemaParser()
        : this(TextWriter.Null) { }

    /// <summary>A parser that writes to <paramref name="warnings"/> what a schema has that trax skips.</summary>
    public GraphQLSchemaParser(TextWriter warnings) => _warnings = warnings;

    public async Task<ApiSchema> ParseAsync(string filePath)
    {
        _customScalars.Clear();
        _abstractTypes.Clear();
        var document = await ReadAsync(filePath).ConfigureAwait(false);

        var schema = new ApiSchema { SourceFile = filePath, SchemaType = "graphql" };
        var definitions = Collect(document);
        var typeDefinitions = definitions.Types;
        var inputDefinitions = definitions.Inputs;
        var enumDefinitions = definitions.Enums;
        var queryTypeName = definitions.QueryTypeName;
        var mutationTypeName = definitions.MutationTypeName;

        MergeAllExtensions(definitions);

        foreach (var kind in definitions.Skipped.Distinct(StringComparer.Ordinal))
            await _warnings
                .WriteLineAsync($"Warning: {kind} definitions are not supported and were skipped.")
                .ConfigureAwait(false);

        AddSharedTypes(
            schema,
            typeDefinitions,
            inputDefinitions,
            enumDefinitions,
            queryTypeName,
            mutationTypeName
        );

        AddOperations(
            schema,
            typeDefinitions,
            inputDefinitions,
            enumDefinitions,
            queryTypeName,
            mutationTypeName
        );

        // Warn about subscriptions
        if (typeDefinitions.ContainsKey("Subscription"))
        {
            await _warnings
                .WriteLineAsync("Warning: Subscription fields are not supported and were skipped.")
                .ConfigureAwait(false);
        }

        return schema;
    }

    private void AddOperations(
        ApiSchema schema,
        Dictionary<string, GraphQLObjectTypeDefinition> typeDefinitions,
        Dictionary<string, GraphQLInputObjectTypeDefinition> inputDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions,
        string queryTypeName,
        string mutationTypeName
    )
    {
        // Parse query operations
        if (typeDefinitions.TryGetValue(queryTypeName, out var queryType))
        {
            ParseOperations(
                queryType,
                OperationKind.Query,
                schema,
                typeDefinitions,
                inputDefinitions,
                enumDefinitions
            );
        }

        // Parse mutation operations
        if (typeDefinitions.TryGetValue(mutationTypeName, out var mutationType))
        {
            ParseOperations(
                mutationType,
                OperationKind.Mutation,
                schema,
                typeDefinitions,
                inputDefinitions,
                enumDefinitions
            );
        }
    }

    // Collect all type definitions first for reference resolution
    private Definitions Collect(GraphQLDocument document)
    {
        var definitions = new Definitions();
        foreach (var definition in document.Definitions)
            ReadDefinition(definitions, definition);
        return definitions;
    }

    private void ReadDefinition(Definitions definitions, ASTNode definition)
    {
        switch (definition)
        {
            case GraphQLSchemaDefinition schemaDef:
                definitions.ReadOperationTypes(schemaDef.OperationTypes);
                break;

            case GraphQLSchemaExtension schemaExt:
                definitions.ReadOperationTypes(schemaExt.OperationTypes);
                break;

            case GraphQLObjectTypeDefinition typeDef:
                definitions.Types[typeDef.Name.StringValue] = typeDef;
                break;

            case GraphQLInputObjectTypeDefinition inputDef:
                definitions.Inputs[inputDef.Name.StringValue] = inputDef;
                break;

            case GraphQLEnumTypeDefinition enumDef:
                if (!BuiltInTypes.Contains(enumDef.Name.StringValue))
                    definitions.Enums[enumDef.Name.StringValue] = enumDef;
                break;

            case GraphQLObjectTypeExtension objectExt:
                definitions.ObjectExtensions.Add(objectExt);
                break;

            case GraphQLInputObjectTypeExtension inputExt:
                definitions.InputExtensions.Add(inputExt);
                break;

            case GraphQLEnumTypeExtension enumExt:
                definitions.EnumExtensions.Add(enumExt);
                break;

            default:
                ReadOtherDefinition(definitions, definition);
                break;
        }
    }

    private void ReadOtherDefinition(Definitions definitions, ASTNode definition)
    {
        switch (definition)
        {
            case GraphQLScalarTypeDefinition scalarDef:
                _customScalars.Add(scalarDef.Name.StringValue);
                break;

            case GraphQLInterfaceTypeDefinition interfaceDef:
                _abstractTypes.Add(interfaceDef.Name.StringValue);
                break;

            case GraphQLUnionTypeDefinition unionDef:
                _abstractTypes.Add(unionDef.Name.StringValue);
                break;

            case GraphQLDirectiveDefinition:
                // A directive changes nothing the generated contract carries.
                break;

            case GraphQLTypeExtension:
                // Scalar, interface and union extensions add nothing a generated type uses.
                break;

            default:
                definitions.Skipped.Add(definition.Kind.ToString());
                break;
        }
    }

    // A type's fields may be spread over its definition and any number of `extend` blocks (a modular
    // schema concatenated, or how HotChocolate prints type extensions). Merge them before anything
    // reads the definitions, so an operation added by `extend type Query` becomes a train.
    private static void MergeAllExtensions(Definitions definitions)
    {
        MergeExtensions(
            definitions.ObjectExtensions,
            definitions.Types,
            e => e.Name.StringValue,
            (def, ext) =>
            {
                if (ext.Fields == null)
                    return;
                def.Fields ??= new GraphQLFieldsDefinition([]);
                def.Fields.Items.AddRange(ext.Fields.Items);
            }
        );
        MergeExtensions(
            definitions.InputExtensions,
            definitions.Inputs,
            e => e.Name.StringValue,
            (def, ext) =>
            {
                if (ext.Fields == null)
                    return;
                def.Fields ??= new GraphQLInputFieldsDefinition([]);
                def.Fields.Items.AddRange(ext.Fields.Items);
            }
        );
        MergeExtensions(
            definitions.EnumExtensions,
            definitions.Enums,
            e => e.Name.StringValue,
            (def, ext) =>
            {
                if (ext.Values == null)
                    return;
                def.Values ??= new GraphQLEnumValuesDefinition([]);
                def.Values.Items.AddRange(ext.Values.Items);
            }
        );
    }

    private void AddSharedTypes(
        ApiSchema schema,
        Dictionary<string, GraphQLObjectTypeDefinition> typeDefinitions,
        Dictionary<string, GraphQLInputObjectTypeDefinition> inputDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions,
        string queryTypeName,
        string mutationTypeName
    )
    {
        // Parse enums
        foreach (var (name, enumDef) in enumDefinitions)
        {
            schema.Enums.Add(
                new ApiEnum
                {
                    Name = NamingConventions.ToPascalCase(name),
                    Values = enumDef.Values.NotNull().Select(v => v.Name.StringValue).ToList(),
                    Description = enumDef.Description?.Value.ToString(),
                    SourceName = name,
                }
            );
        }

        // Collect shared types (non-Query/Mutation/Subscription object types)
        var reservedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            queryTypeName,
            mutationTypeName,
            "Subscription",
        };
        foreach (var (name, typeDef) in typeDefinitions)
        {
            if (reservedTypeNames.Contains(name) || BuiltInTypes.Contains(name))
                continue;

            schema.Types.Add(BuildApiType(typeDef, enumDefinitions));
        }

        // Parse input types as shared types too
        foreach (var (name, inputDef) in inputDefinitions)
        {
            schema.Types.Add(BuildApiTypeFromInput(inputDef, enumDefinitions));
        }
    }

    // The definitions a schema document holds, its `extend` blocks, the kinds it skipped, and the
    // names its schema block gives the query and mutation types.
    private sealed class Definitions
    {
        public Dictionary<string, GraphQLObjectTypeDefinition> Types { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, GraphQLInputObjectTypeDefinition> Inputs { get; } =
            new(StringComparer.Ordinal);
        public Dictionary<string, GraphQLEnumTypeDefinition> Enums { get; } =
            new(StringComparer.Ordinal);
        public string QueryTypeName { get; private set; } = "Query";
        public string MutationTypeName { get; private set; } = "Mutation";
        public List<GraphQLObjectTypeExtension> ObjectExtensions { get; } = [];
        public List<GraphQLInputObjectTypeExtension> InputExtensions { get; } = [];
        public List<GraphQLEnumTypeExtension> EnumExtensions { get; } = [];
        public List<string> Skipped { get; } = [];

        public void ReadOperationTypes(List<GraphQLRootOperationTypeDefinition>? operationTypes)
        {
            if (operationTypes == null)
                return;
            foreach (var op in operationTypes)
            {
                var typeName = op.Type?.Name.StringValue;
                if (typeName == null)
                    continue;

                if (op.Operation == OperationType.Query)
                    QueryTypeName = typeName;
                else if (op.Operation == OperationType.Mutation)
                    MutationTypeName = typeName;
            }
        }
    }

    /// <summary>
    /// Adds each extension's members to the definition it extends. An extension of a type the schema never
    /// defines is refused: dropping it would lose its members without a word, and inventing the type would
    /// generate a contract the schema does not declare.
    /// </summary>
    private static void MergeExtensions<TExtension, TDefinition>(
        List<TExtension> extensions,
        Dictionary<string, TDefinition> definitions,
        Func<TExtension, string> name,
        Action<TDefinition, TExtension> merge
    )
    {
        foreach (var extension in extensions)
        {
            if (!definitions.TryGetValue(name(extension), out var definition))
                throw new InvalidOperationException(
                    $"The schema extends '{name(extension)}' (extend type {name(extension)}), but never defines it."
                        + " Include the file that defines it."
                );
            merge(definition, extension);
        }
    }

    /// <summary>
    /// Reads and parses the SDL, turning a syntax error or an unreadable file into a message that names
    /// the file (and, for a syntax error, the line and column) instead of an unhandled exception.
    /// </summary>
    private static async Task<GraphQLDocument> ReadAsync(string filePath)
    {
        string sdl;
        try
        {
            sdl = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Cannot read the GraphQL schema {filePath}: {ex.Message}",
                ex
            );
        }

        try
        {
            return Parser.Parse(sdl);
        }
        catch (GraphQLParserException ex)
        {
            throw new InvalidOperationException(
                $"{filePath} is not valid GraphQL SDL: {ex.Description} "
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"(line {ex.Location.Line}, column {ex.Location.Column})."
                    ),
                ex
            );
        }
    }

    private void ParseOperations(
        GraphQLObjectTypeDefinition rootType,
        OperationKind kind,
        ApiSchema schema,
        Dictionary<string, GraphQLObjectTypeDefinition> typeDefinitions,
        Dictionary<string, GraphQLInputObjectTypeDefinition> inputDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        if (rootType.Fields == null)
            return;

        // Collect all known type names to detect namespace/type collisions (CS0118).
        // When an operation name matches a type name, the generated namespace segment
        // shadows the type reference — e.g. Flowthru.Trains.Group.AllChats.AllChats
        var knownTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in typeDefinitions.Keys)
            knownTypeNames.Add(NamingConventions.ToPascalCase(name));
        foreach (var name in inputDefinitions.Keys)
            knownTypeNames.Add(NamingConventions.ToPascalCase(name));
        foreach (var name in enumDefinitions.Keys)
            knownTypeNames.Add(NamingConventions.ToPascalCase(name));

        foreach (var field in rootType.Fields)
        {
            var operationName = NamingConventions.ToPascalCase(field.Name.StringValue);

            // Disambiguate if the operation name collides with a known type name
            if (knownTypeNames.Contains(operationName))
            {
                var suffix = kind == OperationKind.Query ? "Query" : "Mutation";
                operationName = $"{operationName}{suffix}";
            }

            // Build input type from arguments
            var inputType = BuildInputTypeFromArguments(
                operationName,
                field.Arguments,
                inputDefinitions,
                enumDefinitions
            );

            // Build output type from return type
            var outputType = BuildOutputType(
                operationName,
                field.Type,
                typeDefinitions,
                enumDefinitions
            );

            schema.Operations.Add(
                new ApiOperation
                {
                    Name = operationName,
                    Kind = kind,
                    Description = field.Description?.Value.ToString(),
                    Group = NamingConventions.DeriveGroupName(field.Name.StringValue),
                    InputType = inputType,
                    OutputType = outputType,
                    SourceName = $"{rootType.Name.StringValue}.{field.Name.StringValue}",
                }
            );
        }
    }

    private ApiType BuildInputTypeFromArguments(
        string operationName,
        GraphQLArgumentsDefinition? arguments,
        Dictionary<string, GraphQLInputObjectTypeDefinition> inputDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        var fields = new List<ApiField>();

        if (arguments != null)
        {
            foreach (var arg in arguments)
            {
                // The `createPlayer(input: PlayerInput!)` idiom: a single argument that is one input object
                // has its fields used directly. A list of them (`createPlayers(inputs: [PlayerInput!]!)`) is
                // not flattened, or a batch would become a single item.
                if (
                    arguments.Count == 1
                    && (arg.Type is GraphQLNonNullType nonNull ? nonNull.Type : arg.Type)
                        is GraphQLNamedType named
                    && inputDefinitions.TryGetValue(named.Name.StringValue, out var inputDef)
                )
                {
                    // Single input argument that is an input type — use that type's fields directly
                    return BuildApiTypeFromInput(
                        inputDef,
                        enumDefinitions,
                        $"{operationName}Input"
                    );
                }

                fields.Add(
                    new ApiField
                    {
                        Name = NamingConventions.ToPascalCase(arg.Name.StringValue),
                        TypeName = ResolveTypeName(arg.Type, enumDefinitions),
                        IsRequired = arg.Type is GraphQLNonNullType,
                        IsNullable = arg.Type is not GraphQLNonNullType,
                        Description = Describe(arg.Description?.Value.ToString(), arg.Type),
                    }
                );
            }
        }

        return new ApiType
        {
            Name = $"{operationName}Input",
            Fields = fields,
            IsBuiltIn = false,
        };
    }

    private ApiType BuildOutputType(
        string operationName,
        GraphQLType graphqlType,
        Dictionary<string, GraphQLObjectTypeDefinition> typeDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        var innerName = GetInnerTypeName(graphqlType);

        // If it's a scalar type, wrap it in an output record
        if (ScalarMap.ContainsKey(innerName) || _customScalars.Contains(innerName))
            return ScalarOutputType(operationName, graphqlType, enumDefinitions);

        // If it's a known object type, reference it
        if (typeDefinitions.ContainsKey(innerName))
            return ObjectOutputType(operationName, graphqlType, innerName);

        // Enum types
        if (enumDefinitions.ContainsKey(innerName))
        {
            var csharpType = ResolveTypeName(graphqlType, enumDefinitions);
            return new ApiType
            {
                Name = $"{operationName}Output",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Value",
                        TypeName = csharpType,
                        IsRequired = true,
                    },
                ],
                IsBuiltIn = false,
            };
        }

        // Unknown type — use object
        return new ApiType
        {
            Name = $"{operationName}Output",
            Fields =
            [
                new ApiField
                {
                    Name = "Value",
                    TypeName = "object",
                    IsRequired = true,
                    Description = $"TODO: Unknown GraphQL type '{innerName}'",
                },
            ],
            IsBuiltIn = false,
        };
    }

    private ApiType ScalarOutputType(
        string operationName,
        GraphQLType graphqlType,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        var csharpType = ResolveTypeName(graphqlType, enumDefinitions);
        return new ApiType
        {
            Name = $"{operationName}Output",
            Fields =
            [
                new ApiField
                {
                    Name = "Value",
                    TypeName = csharpType,
                    IsRequired = true,
                    Description = Describe(description: null, graphqlType),
                },
            ],
            IsBuiltIn = false,
        };
    }

    private static ApiType ObjectOutputType(
        string operationName,
        GraphQLType graphqlType,
        string innerName
    )
    {
        var isListType = IsListType(graphqlType);
        var typeName = NamingConventions.ToPascalCase(innerName);

        if (isListType)
        {
            return new ApiType
            {
                Name = $"{operationName}Output",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Items",
                        TypeName = $"List<{typeName}>",
                        IsRequired = true,
                    },
                ],
                IsBuiltIn = false,
            };
        }

        return new ApiType
        {
            Name = typeName,
            Fields = [],
            IsBuiltIn = true,
        };
    }

    private ApiType BuildApiType(
        GraphQLObjectTypeDefinition typeDef,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        var fields = new List<ApiField>();

        if (typeDef.Fields != null)
        {
            foreach (var field in typeDef.Fields)
            {
                fields.Add(
                    new ApiField
                    {
                        Name = NamingConventions.ToPascalCase(field.Name.StringValue),
                        TypeName = ResolveTypeName(field.Type, enumDefinitions),
                        IsRequired = field.Type is GraphQLNonNullType,
                        IsNullable = field.Type is not GraphQLNonNullType,
                        Description = Describe(field.Description?.Value.ToString(), field.Type),
                    }
                );
            }
        }

        return new ApiType
        {
            Name = NamingConventions.ToPascalCase(typeDef.Name.StringValue),
            Fields = fields,
            IsBuiltIn = false,
            SourceName = typeDef.Name.StringValue,
        };
    }

    private ApiType BuildApiTypeFromInput(
        GraphQLInputObjectTypeDefinition inputDef,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions,
        string? nameOverride = null
    )
    {
        var fields = new List<ApiField>();

        if (inputDef.Fields != null)
        {
            foreach (var field in inputDef.Fields)
            {
                fields.Add(
                    new ApiField
                    {
                        Name = NamingConventions.ToPascalCase(field.Name.StringValue),
                        TypeName = ResolveTypeName(field.Type, enumDefinitions),
                        IsRequired = field.Type is GraphQLNonNullType,
                        IsNullable = field.Type is not GraphQLNonNullType,
                        Description = Describe(field.Description?.Value.ToString(), field.Type),
                    }
                );
            }
        }

        return new ApiType
        {
            Name = nameOverride ?? NamingConventions.ToPascalCase(inputDef.Name.StringValue),
            Fields = fields,
            IsBuiltIn = false,
            SourceName = inputDef.Name.StringValue,
        };
    }

    private string ResolveTypeName(
        GraphQLType graphqlType,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        return graphqlType switch
        {
            GraphQLNonNullType nonNull => ResolveTypeName(nonNull.Type, enumDefinitions),
            GraphQLListType list => $"List<{ResolveTypeName(list.Type, enumDefinitions)}>",
            GraphQLNamedType named => ResolveNamedType(named.Name.StringValue, enumDefinitions),
            _ => "object",
        };
    }

    private string ResolveNamedType(
        string name,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        if (ScalarMap.TryGetValue(name, out var csharpType))
            return csharpType;

        if (enumDefinitions.ContainsKey(name))
            return NamingConventions.ToPascalCase(name);

        if (_customScalars.Contains(name))
            return "string";

        if (_abstractTypes.Contains(name))
            return "object";

        // Custom type reference — use PascalCase name
        return NamingConventions.ToPascalCase(name);
    }

    /// <summary>
    /// A property's description, with a TODO appended when its type is one the generator could only
    /// approximate: a custom scalar written as <c>string</c>, or an interface or union written as <c>object</c>.
    /// </summary>
    private string? Describe(string? description, GraphQLType graphqlType)
    {
        var inner = GetInnerTypeName(graphqlType);
        string? note = null;
        if (!ScalarMap.ContainsKey(inner) && _customScalars.Contains(inner))
            note = $"TODO: custom scalar '{inner}' is generated as string; map it to a C# type.";
        else if (_abstractTypes.Contains(inner))
            note =
                $"TODO: interface or union '{inner}' is generated as object; map it to a C# type.";

        if (note is null)
            return description;
        return string.IsNullOrWhiteSpace(description) ? note : $"{description} {note}";
    }

    private static string GetInnerTypeName(GraphQLType graphqlType)
    {
        return graphqlType switch
        {
            GraphQLNonNullType nonNull => GetInnerTypeName(nonNull.Type),
            GraphQLListType list => GetInnerTypeName(list.Type),
            GraphQLNamedType named => named.Name.StringValue,
            _ => "Unknown",
        };
    }

    private static bool IsListType(GraphQLType graphqlType)
    {
        return graphqlType switch
        {
            GraphQLNonNullType nonNull => IsListType(nonNull.Type),
            GraphQLListType => true,
            _ => false,
        };
    }
}
