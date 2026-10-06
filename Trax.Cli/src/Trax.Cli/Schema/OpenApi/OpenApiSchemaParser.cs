using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Readers.Exceptions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Schema.OpenApi;

public partial class OpenApiSchemaParser : ISchemaParser
{
    private static readonly HashSet<string> QueryMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
    };

    private readonly Dictionary<string, ApiType> _resolvedTypes = new();
    private readonly Dictionary<string, ApiEnum> _resolvedEnums = new();

    // Names the parser invents (promoted inline objects and enums) are made unique against these, and
    // component names are claimed up front, so an invented name never takes a component's. Ignoring
    // case, since each becomes a file name. Schema names themselves are never renamed: two that
    // collide are both kept and SchemaNames.Validate refuses them.
    private readonly HashSet<string> _usedTypeNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _componentNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _usedOperationNames = new(StringComparer.OrdinalIgnoreCase);

    // Components that only name a primitive (PlayerId: {type: string, format: uuid}), by C# name, mapped to
    // the C# type a reference to them is written as. They are not models.
    private readonly Dictionary<string, string> _primitiveAliases = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the document, turning what the reader throws (a file that is not OpenAPI at all, an
    /// OpenAPI version it cannot read, a YAML or JSON syntax error, an unreadable file) into a message
    /// that names the file, instead of an unhandled exception.
    /// </summary>
    private static (OpenApiDocument? Document, OpenApiDiagnostic Diagnostic) Read(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var document = new OpenApiStreamReader().Read(stream, out var diagnostic);
            return (document, diagnostic);
        }
        catch (OpenApiUnsupportedSpecVersionException ex)
        {
            throw new InvalidOperationException(
                UnsupportedVersion(filePath, ex.SpecificationVersion),
                ex
            );
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Cannot read {filePath} as an OpenAPI document: {ex.Message}",
                ex
            );
        }
    }

    internal static string UnsupportedVersion(string filePath, string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? $"{filePath} is not an OpenAPI document: it has no 'openapi' or 'swagger' version field. "
                + "trax generate reads OpenAPI 2.0 (Swagger) and 3.0."
            : $"{filePath} is OpenAPI {version}, which trax generate cannot read. It reads OpenAPI 2.0 "
                + "(Swagger) and 3.0. An ASP.NET Core 10 API writes 3.1 by default; set "
                + "options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0 in AddOpenApi to export 3.0.";

    public ApiSchema Parse(string filePath)
    {
        var (document, diagnostic) = Read(filePath);

        // A syntax error leaves a document with no paths at all rather than none, so both are a
        // document that could not be read.
        if (document?.Paths == null)
        {
            var errors = string.Join(
                Environment.NewLine,
                diagnostic.Errors.Select(e => $"  {e.Pointer} - {e.Message}")
            );
            throw new InvalidOperationException(
                $"Cannot read {filePath} as an OpenAPI document:{Environment.NewLine}{errors}"
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
            foreach (var rawName in document.Components.Schemas.Keys)
            {
                var pascal = ComponentName(rawName);
                _componentNames.Add(pascal);
                _usedTypeNames.Add(pascal);
            }

            // Known before any field is resolved, so a reference resolves the same whichever order the
            // components are declared in.
            foreach (var (rawName, componentSchema) in document.Components.Schemas)
                if (MapPrimitiveAlias(componentSchema) is { } primitive)
                    _primitiveAliases.TryAdd(ComponentName(rawName), primitive);

            var componentSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rawName, componentSchema) in document.Components.Schemas)
            {
                var name = NamingConventions.SimplifySchemaName(rawName);

                // A second component with the same C# name (Billing.Dto and Shipping.Dto, or Status
                // and status) would resolve to the first. Keep it as its own definition so the
                // refusal can name both; the generator never gets that far.
                if (!componentSources.TryAdd(ComponentName(rawName), rawName))
                {
                    AddCollidingComponent(schema, ComponentName(rawName), rawName, componentSchema);
                    continue;
                }
                if (_primitiveAliases.ContainsKey(ComponentName(rawName)))
                    continue;
                if (
                    componentSchema.Enum != null
                    && componentSchema.Enum.Count > 0
                    && componentSchema.Type == "string"
                )
                {
                    var apiEnum = ResolveEnum(name, componentSchema, rawName);
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
                        SourceName = rawName,
                    };
                    _resolvedTypes[pascalName] = apiType;
                    if (!schema.Types.Any(t => t.Name == apiType.Name))
                        schema.Types.Add(apiType);
                }
                else
                {
                    var apiType = ResolveSchemaType(name, componentSchema, rawName);
                    if (!apiType.IsBuiltIn && !schema.Types.Any(t => t.Name == apiType.Name))
                        schema.Types.Add(apiType);
                }
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

        // Rewrite references to empty schemas to "object": a zero-field type is not written to Models/
        // (HotChocolate rejects types with zero fields), and whether a reference was resolved before or after
        // the type it names turned out empty depends on declaration order. Run last, so every type is known,
        // the inline ones promoted while resolving included, and every type expression is rewritten.
        var emptyTypeNames = _resolvedTypes
            .Where(kv => kv.Value.Fields.Count == 0)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (emptyTypeNames.Count > 0)
        {
            var allFields = schema
                .Types.Concat(
                    schema.Operations.SelectMany(o => new[] { o.InputType, o.OutputType })
                )
                .SelectMany(t => t.Fields);
            foreach (var field in allFields)
                field.TypeName = ReplaceEmptyTypeRefs(field.TypeName, emptyTypeNames);
        }

        return schema;
    }

    private static string ComponentName(string rawName) =>
        NamingConventions.ToPascalCase(NamingConventions.SimplifySchemaName(rawName));

    private static void AddCollidingComponent(
        ApiSchema schema,
        string name,
        string rawName,
        OpenApiSchema componentSchema
    )
    {
        if (componentSchema.Enum is { Count: > 0 } && componentSchema.Type == "string")
            schema.Enums.Add(
                new ApiEnum
                {
                    Name = name,
                    Values = [],
                    SourceName = rawName,
                }
            );
        else
            schema.Types.Add(new ApiType { Name = name, SourceName = rawName });
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
        // Each field with the name the schema gave it, so converging names can be told apart.
        var fields = new List<(ApiField Field, string Raw)>();

        // Path and query parameters (from both path item and operation)
        var allParams = (pathItem.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .DistinctBy(p => p.Name);

        foreach (var param in allParams)
        {
            fields.Add(
                (
                    new ApiField
                    {
                        Name = NamingConventions.ToPascalCase(param.Name),
                        TypeName = ResolveOpenApiType(param.Schema, param.Name),
                        IsRequired = param.Required,
                        IsNullable = !param.Required,
                        Description = param.Description,
                    },
                    param.Name
                )
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
                var bodyProperties = BodyProperties(bodySchema);

                if (bodyProperties.Count > 0)
                {
                    var requiredProps = BodyRequired(bodySchema);
                    foreach (var (propName, propSchema) in bodyProperties)
                    {
                        fields.Add(
                            (
                                new ApiField
                                {
                                    Name = NamingConventions.ToPascalCase(propName),
                                    TypeName = ResolveOpenApiType(propSchema, propName),
                                    IsRequired = requiredProps.Contains(propName),
                                    IsNullable = !requiredProps.Contains(propName),
                                    Description = propSchema.Description,
                                },
                                propName
                            )
                        );
                    }
                }
                else if (
                    bodySchema.Reference != null
                    && !_primitiveAliases.ContainsKey(ComponentName(bodySchema.Reference.Id))
                )
                {
                    // Reference to a component schema — pull its fields into the input
                    var refType = ResolveSchemaType(
                        NamingConventions.SimplifySchemaName(bodySchema.Reference.Id),
                        bodySchema
                    );
                    var rawNames = RawPropertyNames(bodySchema);
                    fields.AddRange(
                        refType.Fields.Select(f => (f, rawNames.GetValueOrDefault(f.Name, f.Name)))
                    );
                }
                else if (!IsEmptyObject(bodySchema))
                {
                    // An array, a map, a primitive or a oneOf/anyOf body has no properties to spread
                    // into the input, so it is carried whole as one field rather than dropped.
                    var bodyName = RequestBodyName(operation);
                    fields.Add(
                        (
                            new ApiField
                            {
                                Name = NamingConventions.ToPascalCase(bodyName),
                                TypeName = ResolveOpenApiType(bodySchema, operationName + "Body"),
                                IsRequired = operation.RequestBody.Required,
                                IsNullable = !operation.RequestBody.Required,
                                Description = operation.RequestBody.Description,
                            },
                            "(request body)"
                        )
                    );
                }
            }
        }

        // A path parameter the body repeats (PUT /players/{id} with an id in the body) is one value,
        // kept once. Two different schema names that convert to one (update_value and updateValue)
        // are both kept, so SchemaNames.Validate refuses them rather than one vanishing (cli/0002).
        var inputFields = fields
            .DistinctBy(f => (f.Field.Name, f.Raw))
            .Select(f => f.Field)
            .ToList();

        return new ApiType
        {
            Name = $"{operationName}Input",
            Fields = inputFields,
            IsBuiltIn = false,
        };
    }

    /// <summary>The properties a request body declares, those of each allOf member included.</summary>
    private static List<KeyValuePair<string, OpenApiSchema>> BodyProperties(OpenApiSchema body)
    {
        var properties = new List<KeyValuePair<string, OpenApiSchema>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var source in new[] { body }.Concat(body.Reference == null ? body.AllOf ?? [] : [])
        )
        foreach (var property in source.Properties ?? new Dictionary<string, OpenApiSchema>())
            if (seen.Add(property.Key))
                properties.Add(property);
        return properties;
    }

    private static HashSet<string> BodyRequired(OpenApiSchema body) =>
        (body.Required ?? new HashSet<string>())
            .Concat(
                body.Reference == null
                    ? (body.AllOf ?? []).SelectMany(a => a.Required ?? new HashSet<string>())
                    : []
            )
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>An object schema that declares nothing at all, which carries nothing into a train.</summary>
    private static bool IsEmptyObject(OpenApiSchema schema) =>
        schema.Type is null or "object"
        && schema.Properties is not { Count: > 0 }
        && schema.AdditionalProperties is null
        && schema.Items is null
        && schema.AllOf is not { Count: > 0 }
        && schema.OneOf is not { Count: > 0 }
        && schema.AnyOf is not { Count: > 0 };

    /// <summary>
    /// The name of the field a whole request body becomes: <c>x-codegen-request-body-name</c> on the operation,
    /// the Swagger 2.0 body parameter's name, else <c>body</c>.
    /// </summary>
    private static string RequestBodyName(OpenApiOperation operation)
    {
        if (
            operation.Extensions.TryGetValue("x-codegen-request-body-name", out var codegen)
            && codegen is Microsoft.OpenApi.Any.OpenApiString { Value.Length: > 0 } named
        )
            return named.Value;
        if (
            operation.RequestBody.Extensions.TryGetValue("x-bodyName", out var bodyName)
            && bodyName is Microsoft.OpenApi.Any.OpenApiString { Value.Length: > 0 } swagger
        )
            return swagger.Value;
        return "body";
    }

    /// <summary>The PascalCase field name of each property a schema declares, mapped to the name it
    /// was declared with, allOf members included.</summary>
    private static Dictionary<string, string> RawPropertyNames(OpenApiSchema schema)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var raw in (schema.Properties?.Keys ?? []).Concat(
                schema.AllOf?.SelectMany(a => a.Properties?.Keys ?? []) ?? []
            )
        )
            names.TryAdd(NamingConventions.ToPascalCase(raw), raw);
        return names;
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

            // A component that names a primitive returns that value, as an inline scalar response does.
            if (_primitiveAliases.TryGetValue(typeName, out var primitive))
                return new ApiType
                {
                    Name = $"{operationName}Output",
                    Fields =
                    [
                        new ApiField
                        {
                            Name = "Value",
                            TypeName = primitive,
                            IsRequired = true,
                        },
                    ],
                    IsBuiltIn = false,
                };

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

            if (_primitiveAliases.TryGetValue(pascalName, out var primitive))
                return primitive;

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
            return ResolveInlineEnum(
                NamingConventions.ToPascalCase(schema.Title ?? contextName ?? "UnnamedEnum"),
                schema
            );
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

        if (MapPrimitive(schema) is { } mapped)
            return mapped;

        return schema.Type switch
        {
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

    /// <summary>The C# type for a string, integer, number or boolean schema, by its format; otherwise null.</summary>
    private static string? MapPrimitive(OpenApiSchema schema) =>
        schema.Type switch
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
            _ => null,
        };

    /// <summary>
    /// The C# type a component stands for when all it does is name a primitive: a primitive <c>type</c> with no
    /// properties and no composition. A string enum is an enum, not an alias.
    /// </summary>
    private static string? MapPrimitiveAlias(OpenApiSchema schema) =>
        schema.Properties is { Count: > 0 }
        || schema.AllOf is { Count: > 0 }
        || schema.OneOf is { Count: > 0 }
        || schema.AnyOf is { Count: > 0 }
        || (schema.Type == "string" && schema.Enum is { Count: > 0 })
            ? null
            : MapPrimitive(schema);

    /// <summary>
    /// An inline enum is named after its property (or its title), which many schemas repeat with
    /// different values (<c>status</c> on an order and on a ticket). One with the same values reuses
    /// the enum; one with different values, or whose name a component claims, gets a numbered name.
    /// </summary>
    private string ResolveInlineEnum(string baseName, OpenApiSchema schema)
    {
        var values = EnumValues(baseName, schema);
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? baseName : $"{baseName}{n}";
            if (
                _resolvedEnums.TryGetValue(candidate, out var existing)
                && !_componentNames.Contains(candidate)
            )
            {
                if (existing.Values.SequenceEqual(values, StringComparer.Ordinal))
                    return existing.Name;
            }
            else if (_usedTypeNames.Add(candidate))
            {
                return ResolveEnum(candidate, schema).Name;
            }
        }
    }

    /// <summary>
    /// A string enum's members. A <c>null</c> entry is how OpenAPI 3.0 lets a nullable enum hold null, which
    /// the property's nullability already carries, so it is not a member. Any other entry that is not a
    /// string is refused rather than written as the name of the reader's CLR type.
    /// </summary>
    private static List<string> EnumValues(string name, OpenApiSchema schema) =>
        schema
            .Enum.Where(e => e is not Microsoft.OpenApi.Any.OpenApiNull)
            .Select(e =>
                e is Microsoft.OpenApi.Any.OpenApiString s
                    ? s.Value
                    : throw new InvalidOperationException(
                        $"The string enum '{name}' has a value that is not a string (found "
                            + $"{e.GetType().Name.Replace("OpenApi", "").ToLowerInvariant()}). Make every "
                            + "value a string."
                    )
            )
            .Select(v => NamingConventions.ToPascalCase(v))
            .ToList();

    private ApiEnum ResolveEnum(string name, OpenApiSchema schema, string? sourceName = null)
    {
        var pascalName = NamingConventions.ToPascalCase(name);

        if (_resolvedEnums.TryGetValue(pascalName, out var existing))
            return existing;

        var apiEnum = new ApiEnum
        {
            Name = pascalName,
            Values = EnumValues(pascalName, schema),
            Description = schema.Description,
            SourceName = sourceName,
        };

        _resolvedEnums[pascalName] = apiEnum;
        return apiEnum;
    }

    private ApiType ResolveSchemaType(string name, OpenApiSchema schema, string? sourceName = null)
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
            SourceName = sourceName,
        };

        _resolvedTypes[pascalName] = apiType;
        return apiType;
    }

    /// <summary>
    /// Replaces every empty type named in a type expression (<c>Score</c>, <c>List&lt;List&lt;Score&gt;&gt;</c>,
    /// <c>Dictionary&lt;string, Score&gt;</c>) with <c>object</c>. A name followed by type arguments is a framework
    /// generic, never a model.
    /// </summary>
    private static string ReplaceEmptyTypeRefs(string typeName, HashSet<string> emptyTypeNames) =>
        TypeNameIdentifier()
            .Replace(
                typeName,
                m =>
                    emptyTypeNames.Contains(m.Value)
                    && !(
                        m.Index + m.Length < typeName.Length && typeName[m.Index + m.Length] == '<'
                    )
                        ? "object"
                        : m.Value
            );

    [System.Text.RegularExpressions.GeneratedRegex(SchemaNames.Pattern)]
    private static partial System.Text.RegularExpressions.Regex TypeNameIdentifier();

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
