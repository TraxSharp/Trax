using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Schema.OpenApi;

public class OpenApiSchemaParser : ISchemaParser
{
    private static readonly HashSet<string> QueryMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
    };

    private readonly Dictionary<string, ApiType> _resolvedTypes = new();
    private readonly Dictionary<string, ApiEnum> _resolvedEnums = new();
    private readonly HashSet<string> _usedTypeNames = new();
    private readonly HashSet<string> _usedOperationNames = new();

    public ApiSchema Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var reader = new OpenApiStreamReader();
        var document = reader.Read(stream, out var diagnostic);

        if (document == null)
        {
            var errors = string.Join(Environment.NewLine, diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException(
                $"Failed to parse OpenAPI schema:{Environment.NewLine}{errors}"
            );
        }

        if (diagnostic.Errors.Count > 0)
        {
            foreach (var error in diagnostic.Errors)
            {
                Console.Error.WriteLine($"Warning: {error.Pointer} - {error.Message}");
            }
        }

        var schema = new ApiSchema { SourceFile = filePath, SchemaType = "openapi" };

        // Collect component schemas first
        if (document.Components?.Schemas != null)
        {
            foreach (var (rawName, componentSchema) in document.Components.Schemas)
            {
                var name = NamingConventions.SimplifySchemaName(rawName);
                if (
                    componentSchema.Enum != null
                    && componentSchema.Enum.Count > 0
                    && componentSchema.Type == "string"
                )
                {
                    var apiEnum = ResolveEnum(name, componentSchema);
                    if (!schema.Enums.Any(e => e.Name == apiEnum.Name))
                        schema.Enums.Add(apiEnum);
                }
                else if (componentSchema.Type == "array" && componentSchema.Items != null)
                {
                    // Array-type component schemas become wrapper types with an Items field
                    var pascalName = NamingConventions.ToPascalCase(name);
                    var itemTypeName = ResolveOpenApiType(
                        componentSchema.Items,
                        pascalName + "Item"
                    );
                    var apiType = new ApiType
                    {
                        Name = pascalName,
                        Fields =
                        [
                            new ApiField
                            {
                                Name = "Items",
                                TypeName = $"List<{itemTypeName}>",
                                IsRequired = true,
                            },
                        ],
                        IsBuiltIn = false,
                    };
                    _resolvedTypes[pascalName] = apiType;
                    if (!schema.Types.Any(t => t.Name == apiType.Name))
                        schema.Types.Add(apiType);
                }
                else
                {
                    var apiType = ResolveSchemaType(name, componentSchema);
                    if (!apiType.IsBuiltIn && !schema.Types.Any(t => t.Name == apiType.Name))
                        schema.Types.Add(apiType);
                }
            }
        }

        // Rewrite field types that reference empty schemas to "object" —
        // HotChocolate rejects types with zero fields, and ordering during component
        // resolution means some refs may not have been caught inline.
        var emptyTypeNames = _resolvedTypes
            .Where(kv => kv.Value.Fields.Count == 0)
            .Select(kv => kv.Key)
            .ToHashSet();

        foreach (var apiType in schema.Types)
        {
            foreach (var field in apiType.Fields)
            {
                field.TypeName = ReplaceEmptyTypeRefs(field.TypeName, emptyTypeNames);
            }
        }

        // Parse paths/operations (two-pass: resolve names, then build operations)
        var rawOperations =
            new List<(
                string path,
                OpenApiPathItem pathItem,
                OpenApiOperation operation,
                string httpMethod,
                OperationKind kind,
                string originalName,
                string strippedName
            )>();

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var httpMethod = operationType.ToString().ToUpperInvariant();
                var kind = QueryMethods.Contains(httpMethod)
                    ? OperationKind.Query
                    : OperationKind.Mutation;

                var (originalName, strippedName) = DeriveOperationNames(
                    operation,
                    httpMethod,
                    path
                );
                rawOperations.Add(
                    (path, pathItem, operation, httpMethod, kind, originalName, strippedName)
                );
            }
        }

        // Find stripped names that appear more than once — these need their prefix kept
        var strippedNameCounts = rawOperations
            .GroupBy(o => o.strippedName)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var raw in rawOperations)
        {
            var usePrefixed = strippedNameCounts.Contains(raw.strippedName);
            var baseName = usePrefixed ? raw.originalName : raw.strippedName;
            var operationName = EnsureUniqueOperationName(baseName, raw.path);
            var group = DeriveGroup(raw.operation, raw.path);
            var inputType = BuildInputType(operationName, raw.operation, raw.pathItem);
            var outputType = BuildOutputType(operationName, raw.operation);

            schema.Operations.Add(
                new ApiOperation
                {
                    Name = operationName,
                    Kind = raw.kind,
                    Description = raw.operation.Summary ?? raw.operation.Description,
                    Group = group,
                    InputType = inputType,
                    OutputType = outputType,
                    HttpMethod = raw.httpMethod,
                    HttpPath = raw.path,
                }
            );
        }

        // Add any enums discovered during type resolution
        foreach (var apiEnum in _resolvedEnums.Values)
        {
            if (!schema.Enums.Any(e => e.Name == apiEnum.Name))
                schema.Enums.Add(apiEnum);
        }

        // Add any types discovered during type resolution
        foreach (var apiType in _resolvedTypes.Values)
        {
            if (!apiType.IsBuiltIn && !schema.Types.Any(t => t.Name == apiType.Name))
                schema.Types.Add(apiType);
        }

        return schema;
    }

    /// <summary>
    /// Returns (originalName, strippedName) for two-pass collision detection.
    /// originalName: the full PascalCase name with verb prefix intact.
    /// strippedName: the name with HTTP verb prefix removed (or same as original if no prefix).
    /// If stripping would collide with a known type/enum, both return the original.
    /// </summary>
    private (string Original, string Stripped) DeriveOperationNames(
        OpenApiOperation operation,
        string httpMethod,
        string path
    )
    {
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
        {
            var pascal = NamingConventions.ToPascalCase(operation.OperationId);
            var stripped = NamingConventions.StripHttpVerbPrefix(pascal);

            // If stripping would collide with a known type/enum name, keep the original
            if (
                stripped != pascal
                && (_resolvedTypes.ContainsKey(stripped) || _resolvedEnums.ContainsKey(stripped))
            )
                return (pascal, pascal);

            return (pascal, stripped);
        }

        // Synthesize from path segments (no HTTP verb prefix)
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{'))
            .Select(NamingConventions.ToPascalCase);

        var synthesized = string.Join("", segments);

        // If no segments remain (e.g. root path "/"), fall back to "Root"
        if (string.IsNullOrEmpty(synthesized))
            synthesized = "Root";

        var prefixed = NamingConventions.ToPascalCase(httpMethod) + synthesized;

        // If synthesized name collides with a known type/enum, prefix with HTTP method
        if (_resolvedTypes.ContainsKey(synthesized) || _resolvedEnums.ContainsKey(synthesized))
            return (prefixed, prefixed);

        return (prefixed, synthesized);
    }

    private static string DeriveGroup(OpenApiOperation operation, string path)
    {
        // Use first tag if available
        if (operation.Tags is { Count: > 0 })
            return NamingConventions.ToPascalCase(operation.Tags[0].Name);

        // Fall back to first non-parameter path segment
        var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(s => !s.StartsWith('{'));

        if (firstSegment != null)
            return NamingConventions.ToPascalCase(firstSegment);

        return "General";
    }

    private ApiType BuildInputType(
        string operationName,
        OpenApiOperation operation,
        OpenApiPathItem pathItem
    )
    {
        var fields = new List<ApiField>();

        // Path and query parameters (from both path item and operation)
        var allParams = (pathItem.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .DistinctBy(p => p.Name);

        foreach (var param in allParams)
        {
            fields.Add(
                new ApiField
                {
                    Name = NamingConventions.ToPascalCase(param.Name),
                    TypeName = ResolveOpenApiType(param.Schema, param.Name),
                    IsRequired = param.Required,
                    IsNullable = !param.Required,
                    Description = param.Description,
                }
            );
        }

        // Request body
        if (operation.RequestBody?.Content != null)
        {
            var jsonContent = operation.RequestBody.Content.FirstOrDefault(c =>
                c.Key.Contains("json", StringComparison.OrdinalIgnoreCase)
            );

            if (jsonContent.Value?.Schema != null)
            {
                var bodySchema = jsonContent.Value.Schema;

                if (bodySchema.Properties != null)
                {
                    var requiredProps = bodySchema.Required ?? new HashSet<string>();
                    foreach (var (propName, propSchema) in bodySchema.Properties)
                    {
                        fields.Add(
                            new ApiField
                            {
                                Name = NamingConventions.ToPascalCase(propName),
                                TypeName = ResolveOpenApiType(propSchema, propName),
                                IsRequired = requiredProps.Contains(propName),
                                IsNullable = !requiredProps.Contains(propName),
                                Description = propSchema.Description,
                            }
                        );
                    }
                }
                else if (bodySchema.Reference != null)
                {
                    // Reference to a component schema — pull its fields into the input
                    var refType = ResolveSchemaType(
                        NamingConventions.SimplifySchemaName(bodySchema.Reference.Id),
                        bodySchema
                    );
                    fields.AddRange(refType.Fields);
                }
            }
        }

        // Deduplicate fields by name (different raw names can converge after PascalCase)
        fields = fields.DistinctBy(f => f.Name).ToList();

        return new ApiType
        {
            Name = $"{operationName}Input",
            Fields = fields,
            IsBuiltIn = false,
        };
    }

    private ApiType BuildOutputType(string operationName, OpenApiOperation operation)
    {
        // Find the first successful response (200, 201, 204, etc.)
        var successResponse = operation
            .Responses.Where(r => r.Key.StartsWith('2'))
            .OrderBy(r => r.Key)
            .Select(r => r.Value)
            .FirstOrDefault();

        if (successResponse?.Content == null || successResponse.Content.Count == 0)
        {
            return new ApiType
            {
                Name = "Unit",
                Fields = [],
                IsBuiltIn = true,
            };
        }

        var jsonContent = successResponse.Content.FirstOrDefault(c =>
            c.Key.Contains("json", StringComparison.OrdinalIgnoreCase)
        );

        if (jsonContent.Value?.Schema == null)
        {
            return new ApiType
            {
                Name = "Unit",
                Fields = [],
                IsBuiltIn = true,
            };
        }

        var responseSchema = jsonContent.Value.Schema;

        // If it's a $ref, use the referenced type name
        if (responseSchema.Reference != null)
        {
            var typeName = NamingConventions.ToPascalCase(
                NamingConventions.SimplifySchemaName(responseSchema.Reference.Id)
            );

            // If the referenced type has no fields, treat the output as Unit —
            // HotChocolate rejects object types with zero fields
            if (
                _resolvedTypes.TryGetValue(typeName, out var resolved)
                && resolved.Fields.Count == 0
            )
            {
                return new ApiType
                {
                    Name = "Unit",
                    Fields = [],
                    IsBuiltIn = true,
                };
            }

            return new ApiType
            {
                Name = typeName,
                Fields = [],
                IsBuiltIn = true,
            };
        }

        // Array response
        if (responseSchema.Type == "array" && responseSchema.Items != null)
        {
            var itemTypeName = ResolveOpenApiType(responseSchema.Items, operationName + "Item");

            return new ApiType
            {
                Name = $"{operationName}Output",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Items",
                        TypeName = $"List<{itemTypeName}>",
                        IsRequired = true,
                    },
                ],
                IsBuiltIn = false,
            };
        }

        // Inline object
        if (responseSchema.Properties is { Count: > 0 })
        {
            var fields = new List<ApiField>();
            var requiredProps = responseSchema.Required ?? new HashSet<string>();
            foreach (var (propName, propSchema) in responseSchema.Properties)
            {
                fields.Add(
                    new ApiField
                    {
                        Name = NamingConventions.ToPascalCase(propName),
                        TypeName = ResolveOpenApiType(propSchema, propName),
                        IsRequired = requiredProps.Contains(propName),
                        IsNullable = !requiredProps.Contains(propName),
                        Description = propSchema.Description,
                    }
                );
            }

            return new ApiType
            {
                Name = $"{operationName}Output",
                Fields = fields,
                IsBuiltIn = false,
            };
        }

        // Scalar response
        return new ApiType
        {
            Name = $"{operationName}Output",
            Fields =
            [
                new ApiField
                {
                    Name = "Value",
                    TypeName = ResolveOpenApiType(responseSchema),
                    IsRequired = true,
                },
            ],
            IsBuiltIn = false,
        };
    }

    private string ResolveOpenApiType(OpenApiSchema schema, string? contextName = null)
    {
        if (schema.Reference != null)
        {
            var pascalName = NamingConventions.ToPascalCase(
                NamingConventions.SimplifySchemaName(schema.Reference.Id)
            );

            // If the resolved type has no fields, use object instead —
            // HotChocolate rejects both input and output types with zero fields
            if (
                _resolvedTypes.TryGetValue(pascalName, out var resolved)
                && resolved.Fields.Count == 0
            )
                return "object";

            return pascalName;
        }

        // Enum
        if (schema.Enum is { Count: > 0 } && schema.Type == "string")
        {
            var enumName = NamingConventions.ToPascalCase(
                schema.Title ?? contextName ?? "UnnamedEnum"
            );
            ResolveEnum(enumName, schema);
            return enumName;
        }

        // allOf — merge properties
        if (schema.AllOf is { Count: > 0 })
        {
            // Use the first referenced type name or synthesize
            var refSchema = schema.AllOf.FirstOrDefault(s => s.Reference != null);
            if (refSchema != null)
                return NamingConventions.ToPascalCase(
                    NamingConventions.SimplifySchemaName(refSchema.Reference!.Id)
                );

            return "object"; // fallback
        }

        // oneOf / anyOf
        if (schema.OneOf is { Count: > 0 })
        {
            var first = schema.OneOf[0];
            if (first.Reference != null)
                return NamingConventions.ToPascalCase(
                    NamingConventions.SimplifySchemaName(first.Reference.Id)
                );
            return "object";
        }

        if (schema.AnyOf is { Count: > 0 })
        {
            var first = schema.AnyOf[0];
            if (first.Reference != null)
                return NamingConventions.ToPascalCase(
                    NamingConventions.SimplifySchemaName(first.Reference.Id)
                );
            return "object";
        }

        return schema.Type switch
        {
            "string" when schema.Format == "date-time" => "DateTime",
            "string" when schema.Format == "date" => "DateOnly",
            "string" when schema.Format == "uuid" => "Guid",
            "string" when schema.Format == "uri" => "Uri",
            "string" when schema.Format == "binary" => "byte[]",
            "string" => "string",
            "integer" when schema.Format == "int64" => "long",
            "integer" => "int",
            "number" when schema.Format == "float" => "float",
            "number" => "double",
            "boolean" => "bool",
            "array" when schema.Items != null =>
                $"List<{ResolveOpenApiType(schema.Items, contextName)}>",
            "array" => "List<object>",
            "object" when schema.AdditionalProperties != null =>
                $"Dictionary<string, {ResolveOpenApiType(schema.AdditionalProperties, contextName)}>",
            "object" when schema.Properties is { Count: > 0 } && contextName != null =>
                PromoteInlineObject(contextName, schema),
            "object" => "object",
            _ => "object",
        };
    }

    private ApiEnum ResolveEnum(string name, OpenApiSchema schema)
    {
        var pascalName = NamingConventions.ToPascalCase(name);

        if (_resolvedEnums.TryGetValue(pascalName, out var existing))
            return existing;

        var apiEnum = new ApiEnum
        {
            Name = pascalName,
            Values = schema
                .Enum.Select(e =>
                    e is Microsoft.OpenApi.Any.OpenApiString s ? s.Value : e.ToString()!
                )
                .Select(v => NamingConventions.ToPascalCase(v))
                .ToList(),
            Description = schema.Description,
        };

        _resolvedEnums[pascalName] = apiEnum;
        return apiEnum;
    }

    private ApiType ResolveSchemaType(string name, OpenApiSchema schema)
    {
        var pascalName = NamingConventions.ToPascalCase(name);

        if (_resolvedTypes.TryGetValue(pascalName, out var existing))
            return existing;

        var fields = new List<ApiField>();
        var requiredProps = schema.Required ?? new HashSet<string>();

        // Handle allOf by merging
        var propertiesToProcess = schema.Properties ?? new Dictionary<string, OpenApiSchema>();
        if (schema.AllOf is { Count: > 0 })
        {
            foreach (var allOfSchema in schema.AllOf)
            {
                if (allOfSchema.Properties != null)
                {
                    foreach (var prop in allOfSchema.Properties)
                    {
                        propertiesToProcess.TryAdd(prop.Key, prop.Value);
                    }
                }
                if (allOfSchema.Required != null)
                {
                    foreach (var req in allOfSchema.Required)
                        requiredProps.Add(req);
                }
            }
        }

        foreach (var (propName, propSchema) in propertiesToProcess)
        {
            var fieldName = NamingConventions.ToPascalCase(propName);
            // Avoid C# error CS0542: member name cannot match enclosing type name
            if (fieldName == pascalName)
                fieldName += "Value";

            fields.Add(
                new ApiField
                {
                    Name = fieldName,
                    TypeName = ResolveOpenApiType(propSchema, propName),
                    IsRequired = requiredProps.Contains(propName),
                    IsNullable = !requiredProps.Contains(propName) || propSchema.Nullable,
                    Description = propSchema.Description,
                }
            );
        }

        var apiType = new ApiType
        {
            Name = pascalName,
            Fields = fields,
            IsBuiltIn = false,
        };

        _resolvedTypes[pascalName] = apiType;
        return apiType;
    }

    private static string ReplaceEmptyTypeRefs(string typeName, HashSet<string> emptyTypeNames)
    {
        if (emptyTypeNames.Contains(typeName))
            return "object";

        // Handle generic wrappers like List<EmptyType>
        if (
            typeName.StartsWith("List<", StringComparison.Ordinal)
            && typeName.EndsWith(">", StringComparison.Ordinal)
        )
        {
            var inner = typeName[5..^1];
            if (emptyTypeNames.Contains(inner))
                return "List<object>";
        }

        return typeName;
    }

    private string PromoteInlineObject(string contextName, OpenApiSchema schema)
    {
        // If every property is a bare "type: object" with no further structure,
        // the promoted type would have no GraphQL-representable fields (HotChocolate
        // silently ignores System.Object properties). Fall back to "object" instead.
        if (schema.Properties!.Values.All(IsBareObjectSchema))
            return "object";

        var typeName = EnsureUniqueName(NamingConventions.ToPascalCase(contextName));
        ResolveSchemaType(typeName, schema);
        return typeName;
    }

    private static bool IsBareObjectSchema(OpenApiSchema schema) =>
        schema.Type == "object"
        && schema.Properties is not { Count: > 0 }
        && schema.Reference == null
        && schema.AdditionalProperties == null
        && schema.AllOf is not { Count: > 0 }
        && schema.OneOf is not { Count: > 0 }
        && schema.AnyOf is not { Count: > 0 };

    private string EnsureUniqueOperationName(string baseName, string path)
    {
        if (_usedOperationNames.Add(baseName))
            return baseName;

        // Try By{Param1}And{Param2} disambiguation using path parameters
        var pathParams = path.Split('/')
            .Where(s => s.StartsWith('{') && s.EndsWith('}'))
            .Select(s => NamingConventions.ToPascalCase(s[1..^1]))
            .ToList();

        if (pathParams.Count > 0)
        {
            var suffix = "By" + string.Join("And", pathParams);
            var candidate = baseName + suffix;
            if (_usedOperationNames.Add(candidate))
                return candidate;
        }

        // Fall back to numeric suffix
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (_usedOperationNames.Add(candidate))
                return candidate;
        }
    }

    private string EnsureUniqueName(string baseName)
    {
        if (_usedTypeNames.Add(baseName))
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}{i}";
            if (_usedTypeNames.Add(candidate))
                return candidate;
        }
    }
}
