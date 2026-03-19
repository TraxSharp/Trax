using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Generator;
using Trax.Cli.Models;

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

    private static readonly HashSet<string> BuiltInTypes = new()
    {
        "__Schema",
        "__Type",
        "__Field",
        "__InputValue",
        "__EnumValue",
        "__Directive",
        "__DirectiveLocation",
    };

    public ApiSchema Parse(string filePath)
    {
        var sdl = File.ReadAllText(filePath);
        var document = Parser.Parse(sdl);

        var schema = new ApiSchema { SourceFile = filePath, SchemaType = "graphql" };

        // Collect all type definitions first for reference resolution
        var typeDefinitions = new Dictionary<string, GraphQLObjectTypeDefinition>();
        var inputDefinitions = new Dictionary<string, GraphQLInputObjectTypeDefinition>();
        var enumDefinitions = new Dictionary<string, GraphQLEnumTypeDefinition>();

        string? queryTypeName = "Query";
        string? mutationTypeName = "Mutation";

        foreach (var definition in document.Definitions)
        {
            switch (definition)
            {
                case GraphQLSchemaDefinition schemaDef:
                    if (schemaDef.OperationTypes != null)
                    {
                        foreach (var op in schemaDef.OperationTypes)
                        {
                            var typeName = op.Type?.Name.StringValue;
                            if (typeName == null)
                                continue;

                            if (op.Operation == OperationType.Query)
                                queryTypeName = typeName;
                            else if (op.Operation == OperationType.Mutation)
                                mutationTypeName = typeName;
                        }
                    }
                    break;

                case GraphQLObjectTypeDefinition typeDef:
                    typeDefinitions[typeDef.Name.StringValue] = typeDef;
                    break;

                case GraphQLInputObjectTypeDefinition inputDef:
                    inputDefinitions[inputDef.Name.StringValue] = inputDef;
                    break;

                case GraphQLEnumTypeDefinition enumDef:
                    if (!BuiltInTypes.Contains(enumDef.Name.StringValue))
                        enumDefinitions[enumDef.Name.StringValue] = enumDef;
                    break;
            }
        }

        // Parse enums
        foreach (var (name, enumDef) in enumDefinitions)
        {
            schema.Enums.Add(
                new ApiEnum
                {
                    Name = NamingConventions.ToPascalCase(name),
                    Values = enumDef.Values!.Select(v => v.Name.StringValue).ToList(),
                    Description = enumDef.Description?.Value.ToString(),
                }
            );
        }

        // Collect shared types (non-Query/Mutation/Subscription object types)
        var reservedTypeNames = new HashSet<string>
        {
            queryTypeName!,
            mutationTypeName!,
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

        // Parse query operations
        if (typeDefinitions.TryGetValue(queryTypeName!, out var queryType))
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
        if (typeDefinitions.TryGetValue(mutationTypeName!, out var mutationType))
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

        // Warn about subscriptions
        if (typeDefinitions.ContainsKey("Subscription"))
        {
            Console.WriteLine("Warning: Subscription fields are not supported and were skipped.");
        }

        return schema;
    }

    private static void ParseOperations(
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
                }
            );
        }
    }

    private static ApiType BuildInputTypeFromArguments(
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
                // If the argument type is an input object, check if it's a single-arg input type
                var innerTypeName = GetInnerTypeName(arg.Type);
                if (
                    arguments.Count == 1
                    && inputDefinitions.TryGetValue(innerTypeName, out var inputDef)
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
                        Description = arg.Description?.Value.ToString(),
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

    private static ApiType BuildOutputType(
        string operationName,
        GraphQLType graphqlType,
        Dictionary<string, GraphQLObjectTypeDefinition> typeDefinitions,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        var innerName = GetInnerTypeName(graphqlType);

        // If it's a scalar type, wrap it in an output record
        if (ScalarMap.ContainsKey(innerName))
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

        // If it's a known object type, reference it
        if (typeDefinitions.ContainsKey(innerName))
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

    private static ApiType BuildApiType(
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
                        Description = field.Description?.Value.ToString(),
                    }
                );
            }
        }

        return new ApiType
        {
            Name = NamingConventions.ToPascalCase(typeDef.Name.StringValue),
            Fields = fields,
            IsBuiltIn = false,
        };
    }

    private static ApiType BuildApiTypeFromInput(
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
                        Description = field.Description?.Value.ToString(),
                    }
                );
            }
        }

        return new ApiType
        {
            Name = nameOverride ?? NamingConventions.ToPascalCase(inputDef.Name.StringValue),
            Fields = fields,
            IsBuiltIn = false,
        };
    }

    private static string ResolveTypeName(
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

    private static string ResolveNamedType(
        string name,
        Dictionary<string, GraphQLEnumTypeDefinition> enumDefinitions
    )
    {
        if (ScalarMap.TryGetValue(name, out var csharpType))
            return csharpType;

        if (enumDefinitions.ContainsKey(name))
            return NamingConventions.ToPascalCase(name);

        // Custom type reference — use PascalCase name
        return NamingConventions.ToPascalCase(name);
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
