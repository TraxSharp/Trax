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

    public ApiSchema Parse(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var reader = new OpenApiStreamReader();
        var document = reader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join(Environment.NewLine, diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException(
                $"Failed to parse OpenAPI schema:{Environment.NewLine}{errors}"
            );
        }

        var schema = new ApiSchema { SourceFile = filePath, SchemaType = "openapi" };

        // Collect component schemas first
        if (document.Components?.Schemas != null)
        {
            foreach (var (name, componentSchema) in document.Components.Schemas)
            {
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
                else
                {
                    var apiType = ResolveSchemaType(name, componentSchema);
                    if (!apiType.IsBuiltIn && !schema.Types.Any(t => t.Name == apiType.Name))
                        schema.Types.Add(apiType);
                }
            }
        }

        // Parse paths/operations
        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (operationType, operation) in pathItem.Operations)
            {
                var httpMethod = operationType.ToString().ToUpperInvariant();
                var kind = QueryMethods.Contains(httpMethod)
                    ? OperationKind.Query
                    : OperationKind.Mutation;

                var operationName = DeriveOperationName(operation, httpMethod, path);
                var group = DeriveGroup(operation, path);
                var inputType = BuildInputType(operationName, operation, pathItem);
                var outputType = BuildOutputType(operationName, operation);

                schema.Operations.Add(
                    new ApiOperation
                    {
                        Name = operationName,
                        Kind = kind,
                        Description = operation.Summary ?? operation.Description,
                        Group = group,
                        InputType = inputType,
                        OutputType = outputType,
                        HttpMethod = httpMethod,
                        HttpPath = path,
                    }
                );
            }
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

    private static string DeriveOperationName(
        OpenApiOperation operation,
        string httpMethod,
        string path
    )
    {
        if (!string.IsNullOrWhiteSpace(operation.OperationId))
            return NamingConventions.ToPascalCase(operation.OperationId);

        // Synthesize from method + path segments
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{'))
            .Select(NamingConventions.ToPascalCase);

        return NamingConventions.ToPascalCase(httpMethod) + string.Join("", segments);
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
                    TypeName = ResolveOpenApiType(param.Schema),
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
                                TypeName = ResolveOpenApiType(propSchema),
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
                    var refType = ResolveSchemaType(bodySchema.Reference.Id, bodySchema);
                    fields.AddRange(refType.Fields);
                }
            }
        }

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
            var typeName = NamingConventions.ToPascalCase(responseSchema.Reference.Id);
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
            var itemTypeName =
                responseSchema.Items.Reference != null
                    ? NamingConventions.ToPascalCase(responseSchema.Items.Reference.Id)
                    : "object";

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
                        TypeName = ResolveOpenApiType(propSchema),
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

    private string ResolveOpenApiType(OpenApiSchema schema)
    {
        if (schema.Reference != null)
        {
            return NamingConventions.ToPascalCase(schema.Reference.Id);
        }

        // Enum
        if (schema.Enum is { Count: > 0 } && schema.Type == "string")
        {
            var enumName = NamingConventions.ToPascalCase(schema.Title ?? "UnnamedEnum");
            ResolveEnum(enumName, schema);
            return enumName;
        }

        // allOf — merge properties
        if (schema.AllOf is { Count: > 0 })
        {
            // Use the first referenced type name or synthesize
            var refSchema = schema.AllOf.FirstOrDefault(s => s.Reference != null);
            if (refSchema != null)
                return NamingConventions.ToPascalCase(refSchema.Reference!.Id);

            return "object"; // fallback
        }

        // oneOf / anyOf
        if (schema.OneOf is { Count: > 0 })
        {
            var first = schema.OneOf[0];
            if (first.Reference != null)
                return NamingConventions.ToPascalCase(first.Reference.Id);
            return "object";
        }

        if (schema.AnyOf is { Count: > 0 })
        {
            var first = schema.AnyOf[0];
            if (first.Reference != null)
                return NamingConventions.ToPascalCase(first.Reference.Id);
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
            "array" when schema.Items != null => $"List<{ResolveOpenApiType(schema.Items)}>",
            "array" => "List<object>",
            "object" when schema.AdditionalProperties != null =>
                $"Dictionary<string, {ResolveOpenApiType(schema.AdditionalProperties)}>",
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
            fields.Add(
                new ApiField
                {
                    Name = NamingConventions.ToPascalCase(propName),
                    TypeName = ResolveOpenApiType(propSchema),
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
